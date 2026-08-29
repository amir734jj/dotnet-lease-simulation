FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet workload restore LeaseSimulation.slnx \
    && dotnet restore LeaseSimulation.slnx \
    && dotnet publish src/LeaseSimulation.Browser/LeaseSimulation.Browser.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

FROM node:24-alpine AS final
RUN npm install --global http-server
COPY --from=build /app/publish/wwwroot /var/www
EXPOSE 80
ENTRYPOINT ["http-server", "/var/www", "--port", "3000", "--cache", "-1"]