FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MedStorageUser.csproj ./
RUN dotnet restore MedStorageUser.csproj
COPY . .
RUN dotnet publish MedStorageUser.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MedStorageUser.dll"]
