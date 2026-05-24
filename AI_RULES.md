# AI Rules For This Project

## Scope
- Apply these rules for all code changes in this repository.
- Priority: correctness, consistency, and safe execution.

## Required Workflow (Always)
1. Close old app instance by force when needed.
2. Build `Release` output.
3. If any warning/error exists, fix all until clean.
4. Start the new `bin\\Release\\MKLink.exe` to verify.

## Build Command
- Use:
  - `C:\Program Files (x86)\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe MKLink.csproj /p:Configuration=Release /p:Platform=AnyCPU`

## Non-Negotiable Rules
- Target framework: `.NET Framework 4.8`.
- C# language level: `7.3` only.
- Do not introduce external NuGet packages unless explicitly requested.
- Keep architecture and file split currently used in this project.
- Do not break existing tab flows: `Direct` and `Reverse`.

## Runtime Behavior Rules
- `Check mklink` window must stay non-modal and linked live with `MainWindow`.
- Any destructive operation must be explicit and visible in generated cmd flow.
- CMD execution should remain transparent for debugging (show stage and command lines).

## Output Quality Gate
- Build must end with:
  - `0 Warning(s)`
  - `0 Error(s)`
- If not clean, continue fixing before reporting done.

## Communication Style
- Be concise and practical.
- Explain what was changed and why.
- Report exact build status after changes.
