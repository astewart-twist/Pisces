FROM microsoft/dotnet:2.1-sdk AS build

ADD binaries/5.2.5.20/Pisces_5.2.5.20.tar.gz /app
RUN chmod -R a+xrw /app
