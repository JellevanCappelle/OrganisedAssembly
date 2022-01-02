@echo off

set source=%1%
set obj=%1%.obj
set exe=%2%
set libdir=%3%

nasm -f win64 %source% -o %obj%
ld %obj% %libdir%\kernel32.lib -o %exe% --image-base 0
