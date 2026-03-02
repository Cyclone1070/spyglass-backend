const express = require('express');
const axios = require('axios');
const app = express();
const port = 3128;

app.get('/fetch', async (req, res) => {
    const targetUrl = req.query.url;
    if (!targetUrl) {
        return res.status(400).send('Missing url parameter');
    }

    console.log(`Relaying request to: ${targetUrl}`);

    try {
        const response = await axios.get(targetUrl, {
            headers: {
                'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
                'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8',
                'Accept-Language': 'en-US,en;q=0.9',
            },
            timeout: 15000,
            validateStatus: () => true // Allow any status code to be returned
        });

        res.status(response.status).send(response.data);
    } catch (error) {
        console.error(`Error fetching ${targetUrl}:`, error.message);
        res.status(500).send(`Relay error: ${error.message}`);
    }
});

app.listen(port, () => {
    console.log(`Relay server listening at http://localhost:${port}`);
});
