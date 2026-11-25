# C# search engine for free media on the internet

From public domain books to Abandonware/ROMs, all sourced from reputable websites.

Spyglass Backend is a C# application that serves as the backend for the Spyglass search engine. It scrapes search results from a community-maintained list of reputable websites. 

Visit [fmhy.net](fmhy.net) for more information.

## Self-maintenance

Spyglass is maintained by a weekly automated run, one hour after the Sunday
production scrape (01:00 UTC). This repository owns the runbook and the local
gate; the gate also checks the separate
[frontend repository](https://github.com/Cyclone1070/spyglass).

The workflow is:

1. Verify the latest production scrape from read-only logs.
2. Update backend and frontend dependencies within supported major versions.
3. Run the local gate: backend restore/build/tests and frontend
   install/lint/tests/build.
4. Diagnose and repair gate failures locally. If the failure cannot be resolved,
   stop and report instead of releasing.
5. Commit and push only after the gate is green.
6. Deploy, then run read-only canaries against the API, frontend, and favicon.

The gate never writes production data, never calls `POST /api/link` (it deletes
and rebuilds the link collection), and never deploys. Major dependency upgrades
are only taken when the gate passes without code changes; skipped majors are
recorded in the report.

Full procedure: [MAINTENANCE.md](MAINTENANCE.md). Gate:
[scripts/maintenance-gate.sh](scripts/maintenance-gate.sh).

## Dotnet concepts learned and applied

- **ASP.NET Core:** Built a RESTful API backend using ASP.NET Core with a controller-based approach.
- **Web Scraping:** Implemented a sophisticated web scraper using the **AngleSharp** library to parse and extract data from HTML documents.
- **Dependency Injection:** Utilized ASP.NET Core's built-in dependency injection to manage the lifetime of services and promote loosely coupled code.
- **Configuration Management:** Managed application settings and custom scraping rules using `appsettings.json` and the `IOptions` pattern.
- **Asynchronous Programming:** Leveraged `async` and `await` for non-blocking I/O operations, ensuring the application remains responsive during web requests.
- **LINQ:** Made extensive use of LINQ for querying and manipulating collections in a declarative and readable way.
- **`IHttpClientFactory`:** Used `IHttpClientFactory` to create and manage `HttpClient` instances, following best practices for making HTTP requests.
- **Modern C# Features:** Applied modern C# features such as file-scoped namespaces, top-level statements, and primary constructors.
- **Regular Expressions:** Used compiled regular expressions for efficient pattern matching when analyzing HTML content.
- **JSON Serialization/Deserialization:** Handled JSON data for both configuration and data export.
- **Error Handling:** Implemented robust error handling to gracefully manage exceptions and return appropriate HTTP status codes.
