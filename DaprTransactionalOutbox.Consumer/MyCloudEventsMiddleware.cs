using System.Collections.Generic;
using System.Linq;

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dapr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace DaprTransactionalOutbox.Consumer;

internal static class CloudEventPropertyNames
{
    public const string Data = "data";
    public const string DataContentType = "datacontenttype";
    public const string DataBase64 = "data_base64";
}

    /// <summary>
    /// Provides optional settings to the cloud events middleware.
    /// </summary>
    public class MyCloudEventsMiddlewareOptions
    {
        /// <summary>
        /// Gets or sets a value that will determine whether non-JSON textual payloads are decoded.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In the 1.0 release of the Dapr .NET SDK the cloud events middleware would not JSON-decode
        /// a textual cloud events payload. A cloud event payload containing <c>text/plain</c> data 
        /// of <c>"data": "Hello, \"world!\""</c> would result in a request body containing <c>"Hello, \"world!\""</c>
        /// instead of the expected JSON-decoded value of <c>Hello, "world!"</c>.
        /// </para>
        /// <para>
        /// Setting this property to <c>true</c> restores the previous invalid behavior for compatibility.
        /// </para>
        /// </remarks>
        public bool SuppressJsonDecodingOfTextPayloads { get; set; }

        /// <summary>
        /// Gets or sets a value that will determine whether the CloudEvent properties will be forwarded as Request Headers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Setting this property to <c>true</c> will forward all the CloudEvent properties as Request Headers.
        /// For more fine grained control of which properties are forwarded you can use either <see cref="IncludedCloudEventPropertiesAsHeaders"/> or <see cref="ExcludedCloudEventPropertiesFromHeaders"/>.
        /// </para>
        /// <para>
        /// Property names will always be prefixed with 'Cloudevent.' and be lower case in the following format:<c>"Cloudevent.type"</c>
        /// </para>
        /// <para>
        /// ie. A CloudEvent property <c>"type": "Example.Type"</c> will be added as <c>"Cloudevent.type": "Example.Type"</c> request header.
        /// </para>
        /// </remarks>
        public bool ForwardCloudEventPropertiesAsHeaders { get; set; }

        /// <summary>
        /// Gets or sets an array of CloudEvent property names that will be forwarded as Request Headers if <see cref="ForwardCloudEventPropertiesAsHeaders"/> is set to <c>true</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Note: Setting this will only forwarded the listed property names.
        /// </para>
        /// <para>
        /// ie: <c>["type", "subject"]</c>
        /// </para>
        /// </remarks>
        public string[] IncludedCloudEventPropertiesAsHeaders { get; set; }
        
        /// <summary>
        /// Gets or sets an array of CloudEvent property names that will not be forwarded as Request Headers if <see cref="ForwardCloudEventPropertiesAsHeaders"/> is set to <c>true</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ie: <c>["type", "subject"]</c>
        /// </para>
        /// </remarks>
        public string[] ExcludedCloudEventPropertiesFromHeaders { get; set; }
    }

    internal class MyCloudEventsMiddleware
    {
        private const string ContentType = "application/cloudevents+json";

        // These cloudevent properties are either containing the body of the message or 
        // are included in the headers by other components of Dapr earlier in the pipeline 
        private static readonly string[] ExcludedPropertiesFromHeaders =
        {
            CloudEventPropertyNames.DataContentType, CloudEventPropertyNames.Data,
            CloudEventPropertyNames.DataBase64, "pubsubname", "traceparent"
        };

        private readonly RequestDelegate _next;
        private readonly MyCloudEventsMiddlewareOptions _options;

        public MyCloudEventsMiddleware(RequestDelegate next, MyCloudEventsMiddlewareOptions options)
        {
            this._next = next;
            this._options = options;
        }

        public Task InvokeAsync(HttpContext httpContext)
        {
            // This middleware unwraps any requests with a cloud events (JSON) content type
            // and replaces the request body + request content type so that it can be read by a
            // non-cloud-events-aware piece of code.
            //
            // This corresponds to cloud events in the *structured* format:
            // https://github.com/cloudevents/spec/blob/master/http-transport-binding.md#13-content-modes
            //
            // For *binary* format, we don't have to do anything
            //
            // We don't support batching.
            //
            // The philosophy here is that we don't report an error for things we don't support, because
            // that would block someone from implementing their own support for it. We only report an error
            // when something we do support isn't correct.
            if (!MatchesContentType(httpContext, out var charSet))
            {
                return this._next(httpContext);
            }

            return this.ProcessBodyAsync(httpContext, charSet);
        }

        private async Task ProcessBodyAsync(HttpContext httpContext, string charSet)
        {
            JsonElement json;
            if (string.Equals(charSet, Encoding.UTF8.WebName, StringComparison.OrdinalIgnoreCase))
            {
                json = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Request.Body);
            }
            else
            {
                using (var reader =
                       new HttpRequestStreamReader(httpContext.Request.Body, Encoding.GetEncoding(charSet)))
                {
                    var text = await reader.ReadToEndAsync();
                    json = JsonSerializer.Deserialize<JsonElement>(text);
                }
            }

            Stream originalBody;
            Stream body;

            string originalContentType;
            string? contentType;

            // Check whether to use data or data_base64 as per https://github.com/cloudevents/spec/blob/v1.0.1/json-format.md#31-handling-of-data
            // Get the property names by OrdinalIgnoreCase comparison to support case insensitive JSON as the Json Serializer for AspCore already supports it by default.
            var jsonPropNames = json.EnumerateObject().ToArray();

            var dataPropName = jsonPropNames
                .Select(d => d.Name)
                .FirstOrDefault(d => d.Equals(CloudEventPropertyNames.Data, StringComparison.OrdinalIgnoreCase));

            var dataBase64PropName = jsonPropNames
                .Select(d => d.Name)
                .FirstOrDefault(d =>
                    d.Equals(CloudEventPropertyNames.DataBase64, StringComparison.OrdinalIgnoreCase));

            var isDataSet = false;
            var isBinaryDataSet = false;
            JsonElement data = default;

            if (dataPropName != null)
            {
                isDataSet = true;
                data = json.TryGetProperty(dataPropName, out var dataJsonElement) ? dataJsonElement : data;
            }

            if (dataBase64PropName != null)
            {
                isBinaryDataSet = true;
                data = json.TryGetProperty(dataBase64PropName, out var dataJsonElement) ? dataJsonElement : data;
            }

            if (isDataSet && isBinaryDataSet)
            {
                httpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (isDataSet)
            {
                contentType = GetDataContentType(json, out var isJson);

                // If the value is anything other than a JSON string, treat it as JSON. Cloud Events requires
                // non-JSON text to be enclosed in a JSON string.
                isJson |= data.ValueKind != JsonValueKind.String;

                body = new MemoryStream();
                // if (isJson || options.SuppressJsonDecodingOfTextPayloads)
                // {
                //     // Rehydrate body from JSON payload
                //     await JsonSerializer.SerializeAsync<JsonElement>(body, data);
                // }
                // else
                {
                    // Rehydrate body from contents of the string
                    string? text = data.GetString();
                    await using var writer = new HttpResponseStreamWriter(body, Encoding.UTF8);
                    await writer.WriteAsync(text);
                }

                body.Seek(0L, SeekOrigin.Begin);
            }
            else if (isBinaryDataSet)
            {
                // As per the spec, if the implementation determines that the type of data is Binary,
                // the value MUST be represented as a JSON string expression containing the Base64 encoded
                // binary value, and use the member name data_base64 to store it inside the JSON object.
                var decodedBody = data.GetBytesFromBase64();
                body = new MemoryStream(decodedBody);
                body.Seek(0L, SeekOrigin.Begin);
                contentType = GetDataContentType(json, out _);
            }
            else
            {
                body = new MemoryStream();
                contentType = null;
            }

            ForwardCloudEventPropertiesAsHeaders(httpContext, jsonPropNames);

            originalBody = httpContext.Request.Body;
            originalContentType = httpContext.Request.ContentType;

            try
            {
                    httpContext.Request.Body = body;
                httpContext.Request.ContentType = contentType;

                await this._next(httpContext);
            }
            finally
            {
                httpContext.Request.ContentType = originalContentType;
                httpContext.Request.Body = originalBody;
            }
        }

        private void ForwardCloudEventPropertiesAsHeaders(
            HttpContext httpContext,
            IEnumerable<JsonProperty> jsonPropNames)
        {
            if (!_options.ForwardCloudEventPropertiesAsHeaders)
            {
                return;
            }

            var filteredPropertyNames = jsonPropNames
                .Where(d => !ExcludedPropertiesFromHeaders.Contains(d.Name, StringComparer.OrdinalIgnoreCase));

            if (_options.IncludedCloudEventPropertiesAsHeaders != null)
            {
                filteredPropertyNames = filteredPropertyNames
                    .Where(d => _options.IncludedCloudEventPropertiesAsHeaders
                        .Contains(d.Name, StringComparer.OrdinalIgnoreCase));
            }
            else if (_options.ExcludedCloudEventPropertiesFromHeaders != null)
            {
                filteredPropertyNames = filteredPropertyNames
                    .Where(d => !_options.ExcludedCloudEventPropertiesFromHeaders
                        .Contains(d.Name, StringComparer.OrdinalIgnoreCase));
            }

            foreach (var jsonProperty in filteredPropertyNames)
            {
                httpContext.Request.Headers.TryAdd($"Cloudevent.{jsonProperty.Name.ToLowerInvariant()}",
                    jsonProperty.Value.GetRawText().Trim('\"'));
            }
        }

        private static string? GetDataContentType(JsonElement json, out bool isJson)
        {
            var dataContentTypePropName = json
                .EnumerateObject()
                .Select(d => d.Name)
                .FirstOrDefault(d =>
                    d.Equals(CloudEventPropertyNames.DataContentType,
                        StringComparison.OrdinalIgnoreCase));

            string? contentType;

            if (dataContentTypePropName != null 
                && json.TryGetProperty(dataContentTypePropName, out var dataContentType) 
                &&  dataContentType.ValueKind == JsonValueKind.String 
                && MediaTypeHeaderValue.TryParse(dataContentType.GetString(), out var parsed))
            {
                contentType = dataContentType.GetString();
                isJson =
                    parsed.MediaType.Equals("application/json", StringComparison.Ordinal) ||
                    parsed.Suffix.EndsWith("+json", StringComparison.Ordinal);

                // Since S.T.Json always outputs utf-8, we may need to normalize the data content type
                // to remove any charset information. We generally just assume utf-8 everywhere, so omitting
                // a charset is a safe bet.
                if (contentType.Contains("charset"))
                {
                    parsed.Charset = StringSegment.Empty;
                    contentType = parsed.ToString();
                }
            }
            else
            {
                // assume JSON is not specified.
                contentType = "application/json";
                isJson = true;
            }

            return contentType;
        }

        private static bool MatchesContentType(HttpContext httpContext, out string charSet)
        {
            if (httpContext.Request.ContentType == null)
            {
                charSet = null;
                return false;
            }

            // Handle cases where the content type includes additional parameters like charset.
            // Doing the string comparison up front so we can avoid allocation.
            if (!httpContext.Request.ContentType.StartsWith(ContentType))
            {
                charSet = null;
                return false;
            }

            if (!MediaTypeHeaderValue.TryParse(httpContext.Request.ContentType, out var parsed))
            {
                charSet = null;
                return false;
            }

            if (parsed.MediaType != ContentType)
            {
                charSet = null;
                return false;
            }

            charSet = parsed.Charset.Length > 0 ? parsed.Charset.Value : "UTF-8";
            return true;
        }
    }

    /// <summary>
    /// Provides extension methods for <see cref="IApplicationBuilder" />.
    /// </summary>
    public static class MyDaprApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds the cloud events middleware to the middleware pipeline. The cloud events middleware will unwrap
        /// requests that use the cloud events structured format, allowing the event payload to be read directly.
        /// </summary>
        /// <param name="builder">An <see cref="IApplicationBuilder" />.</param>
        /// <returns>The <see cref="IApplicationBuilder" />.</returns>
        public static IApplicationBuilder UseMyCloudEvents(this IApplicationBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return UseMyCloudEvents(builder, new MyCloudEventsMiddlewareOptions());
        }

        /// <summary>
        /// Adds the cloud events middleware to the middleware pipeline. The cloud events middleware will unwrap
        /// requests that use the cloud events structured format, allowing the event payload to be read directly.
        /// </summary>
        /// <param name="builder">An <see cref="IApplicationBuilder" />.</param>
        /// <param name="options">The <see cref="CloudEventsMiddlewareOptions" /> to configure optional settings.</param>
        /// <returns>The <see cref="IApplicationBuilder" />.</returns>
        public static IApplicationBuilder UseMyCloudEvents(this IApplicationBuilder builder, MyCloudEventsMiddlewareOptions options)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.UseMiddleware<MyCloudEventsMiddleware>(options);
            return builder;
        }
    }