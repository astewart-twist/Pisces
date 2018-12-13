FROM microsoft/dotnet:2.1-sdk AS build

ADD binaries/5.2.9.122/Pisces_5.2.9.122.tar.gz /app
ADD binaries/5.2.9.122/CreateGenomeSizeFile_5.2.9.122.tar.gz /app
ADD binaries/5.2.9.122/Hygea_5.2.9.122.tar.gz /app
ADD binaries/5.2.9.122/Psara_5.2.9.122.tar.gz /app
ADD binaries/5.2.9.122/Scylla_5.2.9.122.tar.gz /app
ADD binaries/5.2.9.122/Stitcher_5.2.9.122.tar.gz /app
ADD binaries/5.2.9.122/VariantQualityRecalibration_5.2.9.122.tar.gz /app
ADD binaries/5.2.9.122/VennVcf_5.2.9.122.tar.gz /app

RUN chmod -R a+xrw /app
