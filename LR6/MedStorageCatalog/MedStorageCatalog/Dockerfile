FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MedStorageCatalog.csproj ./
RUN dotnet restore MedStorageCatalog.csproj
COPY . .
RUN dotnet publish MedStorageCatalog.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MedStorageCatalog.dll"]
