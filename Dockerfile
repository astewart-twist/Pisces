FROM microsoft/dotnet:2.1-sdk AS build

ENV VERSION="5.2.5.20"

WORKDIR /app
ADD . /app
RUN tar -xzf binaries/$VERSION/Pisces_$VERSION.tar.gz
