FROM debian:bookworm-slim AS runtime
# .NET's cryptography (admin auth) goes through the system OpenSSL.
RUN apt-get update && apt-get install -y --no-install-recommends libssl3 ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home mortz
WORKDIR /home/mortz
# libsteam_api only finds the steamclient.so shipped next to it through this.
ENV LD_LIBRARY_PATH=/home/mortz

COPY --chown=mortz:mortz . ./
COPY --chown=mortz:mortz --chmod=755 MortzServer ./
# Holds the server ID that Steam favorites find this server by. Mount a volume here to keep it.
RUN install -d -o mortz -g mortz /home/mortz/data
USER mortz
EXPOSE 7777/udp
# Server-browser queries override with --query-port when needed.
EXPOSE 7778/udp
ENTRYPOINT ["./MortzServer"]

FROM runtime AS standalone

FROM runtime AS steam
