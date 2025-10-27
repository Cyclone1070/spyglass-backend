# C# search engine for internet freebies

From public domain books to Abandonware/ROMs, all sourced from reputable websites.

Spyglass Backend is a C# application that serves as the backend for the Spyglass search engine. It scrapes search results from a community-maintained list of reputable websites. 

Visit [fmhy.net](fmhy.net) for more information.

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
