# Feature worktrees

- When implementing a feature, do the work in a **dedicated git worktree** rather than the main working tree.
- Create the worktree under `tmp/worktrees/` (this path is gitignored), mirroring the branch name in the path — slashes create nested directories, which is fine:
  ```
  git worktree add tmp/worktrees/<branch-name> -b <branch-name>
  ```
  `<branch-name>` may contain slashes (e.g. `feature/xyz/abc/test`), producing `tmp/worktrees/feature/xyz/abc/test`.
- Do all of the feature's editing, committing, and testing inside that worktree.
- When the feature is merged or abandoned, clean up the worktree:
  ```
  git worktree remove tmp/worktrees/<branch-name>
  ```
