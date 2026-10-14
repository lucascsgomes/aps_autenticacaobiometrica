# Contexto de build: a raiz do repositório (docker build -f src/Banco.Api/Dockerfile .).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primeiro só os arquivos de projeto: o restore fica em cache enquanto os pacotes não mudam.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Banco.Contratos/Banco.Contratos.csproj src/Banco.Contratos/
COPY src/Banco.Dominio/Banco.Dominio.csproj src/Banco.Dominio/
COPY src/Banco.Infra/Banco.Infra.csproj src/Banco.Infra/
COPY src/Banco.Api/Banco.Api.csproj src/Banco.Api/
RUN dotnet restore src/Banco.Api/Banco.Api.csproj

COPY src/Banco.Contratos src/Banco.Contratos
COPY src/Banco.Dominio src/Banco.Dominio
COPY src/Banco.Infra src/Banco.Infra
COPY src/Banco.Api src/Banco.Api
RUN dotnet publish src/Banco.Api/Banco.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Atrás do proxy do host (Render), senão o IP de todo cliente seria o do proxy e o rate limit dividiria o mesmo balde.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
USER $APP_UID
EXPOSE 8080

# A conexão com o banco, as chaves e as URLs vêm do ambiente do host; nada disso é gravado na imagem.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet Banco.Api.dll"]
