---
name: ani-pr-feedback
description: "Pull and evaluate open PR review comments for this repository. Use when asked to address PR feedback, review open comments, or work through reviewer notes."
allowed-tools: Bash(pwsh tools/Get-OpenPrComments.ps1) Bash(gh pr edit:*) Bash(gh pr view:*) Bash(gh pr comment:*) Bash(gh api graphql:*) Bash(gh api repos/:*) Read Glob Grep
---

# PR Feedback

## PR Comments

!`pwsh tools/Get-OpenPrComments.ps1 && cat tools/pr-comments.md`

## Step 2: Evaluate Each Comment

For each comment:

1. Determine if it's a valid concern that needs fixing
2. If valid — explain the issue, present possible solutions with pros/cons, then wait for the user to choose before implementing
3. If unsure — ask the user before acting
4. Do not assume all comments are valid or silently fix them

## Step 3: Resolve threads and keep the PR current

Don't leave handled comments open — the open-comment list should only ever show feedback you haven't dealt with yet.

- **Non-issue (after the user agrees it isn't one):** reply explaining why, then resolve the thread.
- **Fixed:** once the fix is pushed, changing the line usually makes the bot auto-mark the thread Outdated/Resolved; if a thread is still open, resolve it explicitly.

> **Run each `gh` write as its own standalone command.** The permission system matches on the
> command prefix, so a single `gh api graphql …` / `gh pr comment …` call is auto-approved by the
> repo's allow rules. Wrapping calls in a `for … do … gh api … done` loop (or piping/`&&`-chaining
> them) makes the command start with `for`/another binary instead of `gh`, so it no longer matches
> the rule and gets bounced to the interactive classifier. Resolve threads one call at a time.

- **List unresolved threads** (get the thread ids and the comment id to reply to):
  ```
  gh api graphql -f query='query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){pullRequest(number:$n){reviewThreads(first:100){nodes{id isResolved isOutdated comments(first:1){nodes{databaseId path body}}}}}}}' -F o=<owner> -F r=<repo> -F n=<pr>
  ```
- **Reply to a review comment** (one standalone call; `<commentId>` is the `databaseId` above):
  ```
  gh api repos/<owner>/<repo>/pulls/<pr>/comments/<commentId>/replies -f body='<reply text>'
  ```
- **Resolve a thread** (one standalone call per thread id — `gh` has no direct command, so use the GraphQL mutation):
  ```
  gh api graphql -f query='mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{isResolved}}}' -F id=<threadId>
  ```

**Keep the PR description up to date as you go.** Whenever the branch changes meaningfully (a fix lands, scope shifts, a new behavior is added), edit the PR body with `gh pr edit <num> --body ...` so it always reflects what's actually in the PR. Reviewers and the squash-merge record should never read a stale description.

## Rules

- Do not silently fix comments without presenting them to the user first
- Evaluate reviewer comments independently — reviewers can be wrong
- Skip threads already marked resolved unless the user asks to revisit them
- Resolve each thread as you finish with it (fixed or agreed non-issue), and keep the PR description current
