@echo off
setlocal

REM ─── Configuration ──────────────────────────────────────────────────────
set API_BASE=http://localhost:5152
set IDS=3,4,52,54,6058

REM ─── Test the endpoint ─────────────────────────────────────────────────
echo Testing GET %API_BASE%/api/items/availability?ids=%IDS%
echo.

curl.exe -i -X GET "%API_BASE%/api/items/availability?ids=%IDS%" ^
  -H "Accept: application/json"

echo.
echo.
pause