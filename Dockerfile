# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# copy csproj first for better layer caching
COPY src/BratnavaFC.Domain/BratnavaFC.Domain.csproj src/BratnavaFC.Domain/
COPY src/BratnavaFC.Application/BratnavaFC.Application.csproj src/BratnavaFC.Application/
COPY src/BratnavaFC.Infrastructure/BratnavaFC.Infrastructure.csproj src/BratnavaFC.Infrastructure/
COPY src/BratnavaFC.Api/BratnavaFC.Api.csproj src/BratnavaFC.Api/

RUN dotnet restore src/BratnavaFC.Api/BratnavaFC.Api.csproj

# copy the rest
COPY . .

RUN dotnet publish src/BratnavaFC.Api/BratnavaFC.Api.csproj -c Release -o /app/publish

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Fly expõe a porta 8080 por padrão
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BratnavaFC.Api.dll"]
