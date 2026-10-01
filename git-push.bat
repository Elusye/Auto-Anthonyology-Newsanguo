@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

rem Usage: git-push.bat ["commit message"]   (default message below if omitted)
set "MSG=Update autoanthony_newsanguo"
if not "%~1"=="" set "MSG=%~1"

rem NOTE: keep this file ASCII-only.
rem cmd.exe mis-parses a .bat that combines "chcp 65001" with multi-byte text:
rem commands lose their leading characters ("git" becomes "it") and the script dies.
rem (Same rule as the newsanguo repo's git-push.bat.)

rem Pin the commit identity per-repo (idempotent).
rem This must be set here, not only on the commit command: "git pull --rebase" and
rem "git rebase --continue" create commits internally WITHOUT those flags, and with
rem no identity git aborts with "Committer identity unknown" mid-rebase.
git config --local user.name "Elusye" >nul
git config --local user.email "87292818+Elusye@users.noreply.github.com" >nul
git config --local core.quotepath false >nul

rem Never let git open an editor (would hang a double-clicked .bat).
set "GIT_EDITOR=true"
set "GIT_SEQUENCE_EDITOR=true"

git remote get-url origin >nul 2>&1
if errorlevel 1 (
    echo === No remote 'origin' configured ===
    echo Create the repo on GitHub first, then run once:
    echo     git remote add origin https://github.com/Elusye/REPO.git
    exit /b 1
)

rem A previous failure may have left the repo mid-rebase.
rem Never "git add -A" + commit in that state: it would commit conflict content.
if exist ".git\rebase-merge" goto :finish_rebase
if exist ".git\rebase-apply" goto :finish_rebase

:stage
echo === Staging all changes (.gitignore filters third-party dirs) ===
git add -A
if errorlevel 1 goto :err

git diff --cached --quiet
if not errorlevel 1 (
    echo Nothing to commit.
    goto :pull
)

echo === Committing ===
git commit -m "%MSG%"
if errorlevel 1 goto :err

:pull
git rev-parse --abbrev-ref "@{u}" >nul 2>&1
if errorlevel 1 goto :first_push

echo === Pulling latest from GitHub (rebase) ===
git pull --rebase origin main
if errorlevel 1 goto :err

:push
echo === Pushing to GitHub ===
git push
if errorlevel 1 goto :err
echo === Done ===
goto :end

:first_push
echo === First push (setting upstream) ===
git push -u origin main
if errorlevel 1 goto :err
echo === Done ===
goto :end

:finish_rebase
echo === Unfinished rebase detected, finishing it first ===
git rebase --continue
if errorlevel 1 goto :manual
goto :stage

:manual
echo === Conflicts need a manual fix ===
git status
echo After fixing: git rebase --continue
echo To give up:    git rebase --abort   (then run this script again)
goto :err

:err
echo Failed. Check the output above.
exit /b 1

:end
exit /b 0
