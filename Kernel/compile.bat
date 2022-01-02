@echo off

set source=%1%
set output=%2%

nasm -f bin %source% -o %output%
