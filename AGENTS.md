# Agent Instructions
- Always follow best practices for software development, including writing clean, maintainable code and adhering to coding standards.
- Always read `docs/PLAN.md`, `docs/TODO.md`, `docs/DEBUGGING.md`, and `README.md` at the start of a session.
- Always follow repository `.editorconfig` standards for formatting, style, and naming.
- Provide guidance that is specific to `.NET MAUI` unless the user explicitly asks for another framework.
- Verify MAUI APIs/patterns before recommending them (prefer official Microsoft MAUI/.NET docs and current project code).
- Do not mix in WPF/Xamarin.Forms/Blazor/React patterns unless explicitly requested and clearly labeled as alternatives.
- If uncertain about a MAUI recommendation, say so and confirm before proposing implementation details.
- Keep changes aligned with the planning docs; ask before deviating.
- Keep `docs/PLAN.md`, `docs/TODO.md`, `docs/DEBUGGING.md`, and `README.md` up to date when we make decisions or discuss changes that warrant updates.
- When debugging issues, always pull the latest on-device app log into the repo before analysis:
  `adb -s emulator-5554 exec-out run-as com.companyname.anisprinkles cat files/logs/anisprinkles.log > logs/anisprinkles.device.log`
- As part of confirmation, always dump and review current-process `adb logcat` output for crashes, exceptions, and frame/perf warnings:
  `$appPid = adb -s emulator-5554 shell pidof com.companyname.anisprinkles`
  `adb -s emulator-5554 logcat -v time --pid $appPid -d > logs/adb.device.pid.log`
