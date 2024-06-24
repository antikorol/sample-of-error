FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["ApiGateway.sln", "./"]
COPY ["NuGet.Config", "./"]
COPY src/*/*.csproj ./
COPY tests/*/*.csproj ./
RUN cat ApiGateway.sln \
| grep "\.csproj" \
| awk '{print $4}' \
| sed -e 's/[",]//g' \
| sed 's#\\#/#g' \
| xargs -I {} sh -c 'mkdir -p $(dirname {}) && mv $(basename {}) $(dirname {})/' \
RUN dotnet restore "ApiGateway.sln"
COPY . .
WORKDIR "/src/src/ApiGateway.Service"
RUN dotnet build "ApiGateway.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ApiGateway.Service.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ApiGateway.Service.dll"]
