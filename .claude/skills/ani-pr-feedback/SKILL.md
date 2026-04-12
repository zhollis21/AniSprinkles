---
name: ani-pr-feedback
description: "Pull and evaluate open PR review comments for this repository. Use when asked to address PR feedback, review open comments, or work through reviewer notes."
disable-model-invocation: true
allowed-tools: Bash(pwsh tools/Get-OpenPrComments.ps1) Read Glob Grep
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

## Rules

- Do not silently fix comments without presenting them to the user first
- Evaluate reviewer comments independently — reviewers can be wrong
- Skip threads already marked resolved unless the user asks to revisit them
