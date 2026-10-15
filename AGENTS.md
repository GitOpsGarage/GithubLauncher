# GitOpsGarage fork

- This is the GitOpsGarage-maintained macOS fork of SirDiabo/GithubLauncher.
- Preserve attribution and the upstream license. Do not claim upstream endorsement.
- Every commit must run the installed Overcommit hooks. Never use `--no-verify`,
  `SKIP`, `ONLY`, or `OVERCOMMIT_DISABLE` to bypass checks.
- Install with `bundle install`, `bundle exec overcommit --install`, and review/sign
  configuration with `bundle exec overcommit --sign` and
  `bundle exec overcommit --sign pre-commit` before the first commit.
- Run `bash scripts/check.sh` for source changes. Use `DOTNET_BIN` when the SDK is
  not on PATH. Test native installation changes with local ZIP/TAR/DMG fixtures.
- Published Mac release evidence is not proof of gameplay compatibility. Keep
  native releases, tested CrossOver builds, candidates, and unknown results separate.
- Preserve complete native release payloads; sibling SDK/tool directories can be
  required for a game's first-run setup.
- Never silently accept installer licenses or remove Gatekeeper quarantine.
- Write comments and documentation in English. Reply in the user's language.
