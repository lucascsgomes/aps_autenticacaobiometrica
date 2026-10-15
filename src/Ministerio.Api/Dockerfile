# Contexto de build: a raiz do repositório (docker build -f src/Ministerio.Api/Dockerfile .).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primeiro só os arquivos de projeto: o restore fica em cache enquanto os pacotes não mudam.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Ministerio.Contratos/Ministerio.Contratos.csproj src/Ministerio.Contratos/
COPY src/Ministerio.Dominio/Ministerio.Dominio.csproj src/Ministerio.Dominio/
COPY src/Ministerio.Infra/Ministerio.Infra.csproj src/Ministerio.Infra/
COPY src/Ministerio.Api/Ministerio.Api.csproj src/Ministerio.Api/
RUN dotnet restore src/Ministerio.Api/Ministerio.Api.csproj

COPY src/Ministerio.Contratos src/Ministerio.Contratos
COPY src/Ministerio.Dominio src/Ministerio.Dominio
COPY src/Ministerio.Infra src/Ministerio.Infra
COPY src/Ministerio.Api src/Ministerio.Api
RUN dotnet publish src/Ministerio.Api/Ministerio.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Atrás do proxy do host (Render), senão o IP de todo cliente seria o do proxy e o rate limit dividiria o mesmo balde.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
USER $APP_UID
EXPOSE 8080

# A conexão com a base, as chaves e as URLs vêm do ambiente do host; nada disso é gravado na imagem.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet Ministerio.Api.dll"]
