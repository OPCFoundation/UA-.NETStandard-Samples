#/bin/bash

FILE=/gds/config/Opc.Ua.GlobalDiscoveryServer.Config.xml

if test -f "$FILE"; then
    # if the config file already exists in the exposed config dir the possibly modified version from the host is used
    cp -f /gds/config/Opc.Ua.GlobalDiscoveryServer.Config.xml /gds/Opc.Ua.GlobalDiscoveryServer.Config.xml
else
    # if currently no config file exists in the exposed folder the default config file is copied to the exposed directory
    cp /gds/Opc.Ua.GlobalDiscoveryServer.Config.xml /gds/config/Opc.Ua.GlobalDiscoveryServer.Config.xml
fi

dotnet /gds/NetCoreGlobalDiscoveryServer.dll
