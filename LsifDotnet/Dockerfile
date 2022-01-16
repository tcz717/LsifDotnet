#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG VERSION=1.0.0
WORKDIR /src
COPY ["LsifDotnet/LsifDotnet.csproj", "LsifDotnet/"]
RUN dotnet restore "LsifDotnet/LsifDotnet.csproj"
COPY . .
WORKDIR "/src/LsifDotnet"
RUN dotnet publish "LsifDotnet.csproj" -c Release -o /app/publish -p:Version=${VERSION}

FROM sourcegraph/src-cli:3.34.1 AS src-cli

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=src-cli /usr/bin/src /usr/bin/