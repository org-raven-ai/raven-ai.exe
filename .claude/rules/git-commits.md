# Git commits

- Commit changes with git as you develop features — don't leave work uncommitted.
- Keep commits **small and focused**: one logical change per commit.
- Group only related changes together. Never bundle many unrelated changes into one large commit.
- Write clear commit messages describing the single change.
- Author Claude's own commits as Claude, not the user. **Override the identity per-commit — never change the repo's git config**, since that would also reauthor the user's commits.
  - Use the `-c` flags on each commit so both author and committer are set for that one command only:
    ```
    git -c commit.gpgsign=false -c user.name="Claude" -c user.email="noreply@anthropic.com" commit -m "..."
    ```
  - Do NOT run `git config user.name`/`user.email` (local or global) to set this.
- **Disable GPG signing on Claude-authored commits** with the per-command flag `-c commit.gpgsign=false` (shown above). The repo signs with the user's GPG key via an interactive passphrase prompt, which can't be answered non-interactively, and Claude's commits shouldn't be signed with the user's key anyway. Set this per-command only — never change the repo's `commit.gpgsign` config.