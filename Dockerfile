FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CloudHub.Api/CloudHub.Api.csproj ./
RUN dotnet restore CloudHub.Api.csproj
COPY src/CloudHub.Api/Program.cs ./
COPY src/CloudHub.Api/PcChecks.cs ./
COPY src/CloudHub.Api/MonitoringModels.cs ./
COPY src/CloudHub.Api/wwwroot/ ./wwwroot/
RUN dotnet publish CloudHub.Api.csproj -c Release --no-restore -o /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CloudHub.Api.dll"]
