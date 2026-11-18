FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ src/
COPY KnockKnockClient/Assets/Shared/ KnockKnockClient/Assets/Shared/
RUN dotnet publish src/KnockKnockArena.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

COPY data/arena.json data/weapons.json data/pickups.json /app/content/
ENV KKARENA_MAP=/app/content/arena.json \
    KKARENA_WEAPONS=/app/content/weapons.json \
    KKARENA_PICKUPS=/app/content/pickups.json

ENV KKARENA_DATA_DIR=/data
VOLUME /data

EXPOSE 36363/tcp
EXPOSE 36363/udp
EXPOSE 8080/tcp

ENTRYPOINT ["dotnet", "KnockKnockArena.Server.dll"]
