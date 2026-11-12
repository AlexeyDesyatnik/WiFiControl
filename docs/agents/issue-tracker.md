# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`.
- **Read an issue**: `gh issue view <number> --json number,title,body,labels,comments`.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Make an issue a sub-issue of a parent**: `gh issue create --parent <parent> ...`, or `gh issue edit <parent> --add-sub-issue <child>` afterwards (`gh` 2.94+). Older `gh`: `gh api --method POST repos/<owner>/<repo>/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>` (database id, as in **Dependencies** below). Without sub-issues, put `Part of #<parent>` at the top of the child body.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v`; `gh` does this automatically when run inside a clone.

For multi-line issue bodies and comments, write the text to a UTF-8 file and pass it with `--body-file <path>` to `gh issue create`, `gh issue edit`, or `gh issue comment` as appropriate.

Follow [AGENTS.md](../../AGENTS.md#issue-tracker) when finishing an issue.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Read it as in **Read an issue** above.

## Dependencies

- Add a blocker with `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`.
- Get the blocker's numeric database ID with `gh api repos/<owner>/<repo>/issues/<n> --jq .id`.
- Check open blockers with GitHub's `issue_dependencies_summary.blocked_by`. Where native dependencies are unavailable, use a `Blocked by: #<n>, #<n>` line at the top of the child issue. The child is unblocked when every blocker is closed.
