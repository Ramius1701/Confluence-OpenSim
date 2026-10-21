#!/bin/sh

case "$1" in

 'clean')
    dotnet bin/prebuild.dll /file prebuild.xml /clean

  ;;


  'autoclean')

    echo y|dotnet bin/prebuild.dll /file prebuild.xml /clean

  ;;



  *)

    cp bin/System.Drawing.Common.dll.linux bin/System.Drawing.Common.dll
    dotnet bin/prebuild.dll /target vs2022 /targetframework net10_0 /excludedir = "obj | bin" /file prebuild.xml
    # Jolt physics and the Phlox script engine are hand-maintained projects that prebuild does not generate;
    # add them to the solution so a normal build builds them too (both output to bin/). --in-root: a solution
    # folder named after their path would clash with the OpenSim project.
    dotnet sln OpenSim.sln add --in-root \
      OpenSim/Region/PhysicsModules/Jolt.Backend/OpenSim.Region.PhysicsModules.Jolt.Backend.csproj \
      OpenSim/Region/PhysicsModules/Jolt.Vehicles/OpenSim.Region.PhysicsModules.Jolt.Vehicles.csproj \
      OpenSim/Region/PhysicsModules/Jolt/OpenSim.Region.PhysicsModules.Jolt.csproj \
      OpenSim/Region/PhysicsModules/Jolt.Diagnostics/OpenSim.Region.PhysicsModules.Jolt.Diagnostics.csproj \
      OpenSim/Region/ScriptEngine/InWorldz.Phlox/InWorldz.Phlox.csproj \
      OpenSim/Region/ScriptEngine/Phlox.ScriptEngine/Phlox.ScriptEngine.csproj
    echo "dotnet build -c Release OpenSim.sln" > compile.sh
    chmod +x compile.sh

  ;;

esac
