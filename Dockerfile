# 1. SDK ilə layihəni build edirik
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Trade Bot.csproj", "./"]
RUN dotnet restore "Trade Bot.csproj"
COPY . .
RUN dotnet build "Trade Bot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Trade Bot.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Sonda ancaq Runtime ilə layihəni qaldırırıq (yüngül olsun deyə)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
ENTRYPOINT ["dotnet", "Trade Bot.dll"]