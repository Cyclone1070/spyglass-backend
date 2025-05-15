# spyglass-backend

![Screenshot 2025-05-15 at 21 54 27](https://github.com/user-attachments/assets/1a18b342-576c-4c89-9cd9-41d60639164a)

Preview demo with rate limited google api key: https://spyglass-cyclone.vercel.app

A backend for custom search engine written in go with test driven development.

# How It Works

The goal of the program is to scape the search results from the websites that offer such functionality (e.g., https://www.imdb.com/find/?s=tt&q={query}&ref_=nv_sr_sm).

There are two main parts to the backend: cron job and http server.

- The cron job is going scrape the structure of the websites in the provided list to identify the css selector path to the content we want to scrape, in this case it is the card list structure that the search functionality of websites often return.
- The http server is going to accept RESTful api calls from the frontend, then send the query to all websites in the list and scrape the results based on the structure found by the cron job. Results from multiple sites will then be aggregated, sorted and ranked before returned to the front end in JSON.

