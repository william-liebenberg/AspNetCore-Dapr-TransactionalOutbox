import http from 'k6/http';
import { check } from 'k6';

export const options = {
    stages: [
        { duration: '10s', target: 50 },  // Ramp-up to 50 users
        { duration: '30s', target: 500 },   // Ramp-up to 500 users
        { duration: '10s', target: 0 },   // Ramp-down
    ],
};

// Sample descriptions to randomize from
const descriptions = ['item1', 'item2', 'item3', 'item4', 'item5', 'item6'];

export default function () {
    const payload = JSON.stringify({
        Price: parseFloat((Math.random() * 10 + 1).toFixed(2)),  // Random price between 1.00 and 11.00
        Description: descriptions[Math.floor(Math.random() * descriptions.length)],
    });

    const params = {
        headers: {
            'Content-Type': 'application/json',
        },
    };

    const res = http.post('http://localhost:5325/order-via-tx-outbox', payload, params);

    check(res, {
        'status is 200': (r) => r.status === 200,
    });
}
