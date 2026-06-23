@echo off
REM ============================================================
REM  Remove compilation artifacts (A.MAC, A.SAV) from git.
REM  Files stay on disk - only untracked from git.
REM  run.bat and emulator are NOT touched.
REM ============================================================

cd /d D:\UKNC

echo.
echo === Tracked files in _projectUKNC ===
git ls-files _projectUKNC/

echo.
echo === Removing A.MAC and A.SAV from git (files stay on disk) ===
git rm --cached _projectUKNC/A.MAC 2>nul
git rm --cached _projectUKNC/A.SAV 2>nul

echo.
echo === Status (expect: .gitignore modified, A.SAV/A.MAC deleted) ===
git status

echo.
echo If status looks OK (no run.bat!) - press any key to commit and push.
echo If something is wrong - close window (Ctrl+C).
pause

git add .gitignore
git commit -m "Ignore compilation artifacts (A.MAC, A.SAV)"
git push origin main

echo.
echo === Done ===
pause
