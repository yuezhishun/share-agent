@echo off
setlocal
chcp 65001 >nul
title Codex 环境变量切换并启动

echo ==============================
echo    Codex 账号选择与启动
echo ==============================
echo.
echo 1 - 个人账号   (CODEX_HOME = C:\Users\yueyuan\.codex)
echo 2 - 工作账号   (CODEX_HOME = C:\Users\yueyuan\.codex-work)
echo.
choice /c 12 /n /m "请直接按 1 或 2: "

:: 设置变量（同时影响当前会话和永久环境）
if errorlevel 2 (
    set "CODEX_HOME=C:\Users\yueyuan\.codex-work"
    setx CODEX_HOME "C:\Users\yueyuan\.codex-work" >nul
    echo 已切换到工作账号配置。
) else if errorlevel 1 (
    set "CODEX_HOME=C:\Users\yueyuan\.codex"
    setx CODEX_HOME "C:\Users\yueyuan\.codex" >nul
    echo 已切换到个人账号配置。
)

echo.
echo 当前 CODEX_HOME = %CODEX_HOME%
echo 正在启动 Codex...
echo.

:: 在当前窗口中运行 codex（前台交互式）
codex --dangerously-bypass-approvals-and-sandbox

:: codex 退出后，显示提示（可选）
echo.
echo Codex 已退出。按任意键关闭此窗口...
pause >nul