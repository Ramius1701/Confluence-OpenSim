@echo OFF

copy bin\System.Drawing.Common.dll.win bin\System.Drawing.Common.dll

dotnet bin\prebuild.dll /target vs2022 /targetframework net10_0 /excludedir = "obj | bin" /file prebuild.xml

rem Jolt physics and the Phlox script engine are hand-maintained projects that prebuild does not generate;
rem add them to the solution so a normal build builds them too (both output to bin\). --in-root: a solution
rem folder named after their path would clash with the OpenSim project.
dotnet sln OpenSim.sln add --in-root ^
  OpenSim\Region\PhysicsModules\Jolt.Backend\OpenSim.Region.PhysicsModules.Jolt.Backend.csproj ^
  OpenSim\Region\PhysicsModules\Jolt.Vehicles\OpenSim.Region.PhysicsModules.Jolt.Vehicles.csproj ^
  OpenSim\Region\PhysicsModules\Jolt\OpenSim.Region.PhysicsModules.Jolt.csproj ^
  OpenSim\Region\ScriptEngine\InWorldz.Phlox\InWorldz.Phlox.csproj ^
  OpenSim\Region\ScriptEngine\Phlox.ScriptEngine\Phlox.ScriptEngine.csproj

    @echo Creating compile.bat
rem To compile in release mode
    @echo dotnet build --configuration Release OpenSim.sln > compile.bat
rem To compile in debug mode comment line (add rem to start) above and uncomment next (remove rem)
rem    @echo dotnet build --configuration Debug OpenSim.sln > compile.bat
:done


if exist "bin\addin-db-002" (
	del /F/Q/S bin\addin-db-002 > NUL
	rmdir /Q/S bin\addin-db-002
	)
if exist "bin\addin-db-004" (
	del /F/Q/S bin\addin-db-004 > NUL
	rmdir /Q/S bin\addin-db-004
	)	