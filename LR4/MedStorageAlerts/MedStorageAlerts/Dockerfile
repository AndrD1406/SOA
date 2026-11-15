FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MedStorageAlerts.csproj ./
RUN dotnet restore MedStorageAlerts.csproj
COPY . .
RUN dotnet publish MedStorageAlerts.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MedStorageAlerts.dll"]
