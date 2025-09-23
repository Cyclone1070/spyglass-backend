#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/sdk:9.0-preview AS build
WORKDIR /src
COPY ["spyglass-backend.csproj", "."]
RUN dotnet restore "./spyglass-backend.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./spyglass-backend.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "./spyglass-backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-preview AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "spyglass-backend.dll"]
