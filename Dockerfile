FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ArrDash/ArrDash.csproj src/ArrDash/
RUN dotnet restore src/ArrDash/ArrDash.csproj
COPY src/ArrDash/ src/ArrDash/
RUN dotnet publish src/ArrDash/ArrDash.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ARRDASH_CONFIG_PATH=/config
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ArrDash.dll"]
