@echo off
setlocal
chcp 65001 >nul
title Codex 账号选择器

set "BASH_EXE=C:\Program Files\Git\bin\bash.exe"

echo ==============================
echo    Codex 账号选择与启动
echo ==============================
echo.
echo 1 - 个人账号   (CODEX_HOME = C:\Users\yueyuan\.codex)
echo 2 - 工作账号   (CODEX_HOME = C:\Users\yueyuan\.codex-work)
echo.
choice /c 12 /n /m "请直接按 1 或 2: "

if errorlevel 2 (
    set "CODEX_HOME=C:\Users\yueyuan\.codex-work"
    setx CODEX_HOME "C:\Users\yueyuan\.codex-work" >nul
    echo 已切换到工作账号配置。
) else if errorlevel 1 (
    set "CODEX_HOME=C:\Users\yueyuan\.codex"
    setx CODEX_HOME "C:\Users\yueyuan\.codex" >nul
    echo 已切换到个人账号配置。
)

:: 转换路径为 Unix 风格
set "CODEX_HOME_UNIX=%CODEX_HOME:\=/%"
set "CODEX_HOME_UNIX=%CODEX_HOME_UNIX:C:=/c%"

:: 弹出新窗口运行 Codex
start "Codex" "%BASH_EXE%" -c "export CODEX_HOME='%CODEX_HOME_UNIX%'; codex --dangerously-bypass-approvals-and-sandbox; echo 'Codex 已退出，按任意键关闭此窗口...'; read -n 1"

:: 可选：原 CMD 窗口自动退出（界面更干净）
exit