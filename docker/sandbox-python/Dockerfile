FROM python:3.12-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends bash coreutils \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --shell /bin/bash sandbox

WORKDIR /workspace
COPY entrypoint.sh /entrypoint.sh
USER sandbox

ENTRYPOINT ["/bin/bash", "/entrypoint.sh"]
