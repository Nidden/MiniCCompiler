@echo off
chcp 65001 >nul
REM ============================================================
REM  Коммит и пуш проекта MiniCCompiler на GitHub
REM  Запускать из корня репозитория (где папка CompMacro11)
REM ============================================================

cd /d D:\UKNC

echo.
echo === Файлы, изменённые за последние сессии ===
echo   CompMacro11\CodeGen.cs           (тернарный, знаковое деление, новые ф-ции, убраны флипы)
echo   CompMacro11\CodeGen.Runtime.cs   (vsync/sin256/cos256, убран RTSPRF/REVTAB)
echo   CompMacro11\Lexer.cs             (токен '?')
echo   CompMacro11\Parser.cs            (тернарный, глоб. переменные через запятую)
echo   CompMacro11\AST.cs               (TernaryExpr)
echo   CompMacro11\Form1.cs             (VS-редактор, путь A.SAV, справка по цветам)
echo   CompMacro11\Spriteeditor.cs      (палитра)
echo   UKNC_CODES.md                    (таблица цветов, окт/дес)
echo.
echo Убедись, что все эти файлы заменены последними версиями!
echo.
pause

echo.
echo === Текущий статус ===
git status --short

echo.
echo === Добавляю изменения ===
git add -A

echo.
set /p MSG="Сообщение коммита (Enter = стандартное): "
if "%MSG%"=="" set MSG=Ternary operator, signed division SXT, vsync/sin256/cos256/abs/min/max/clamp, remove sprite flips, color palette, IDE improvements

git commit -m "%MSG%"

echo.
echo === Пуш на GitHub ===
git push origin main

echo.
echo === Готово ===
echo Если попросит логин - вставь username и Personal Access Token вместо пароля.
pause
