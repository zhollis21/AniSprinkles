---
name: ani-kickoff
description: "Start work on a GitHub issue the right way: pull the issue, verify it isn't stale or already fixed, analyze solutions independently of whatever the issue proposes, surface tradeoffs, ask clarifying questions, and agree a plan before any code exists. Use whenever the user names issue numbers to work on — \"let's do #112\", \"start issue 63\", \"pick up 52 and 62\" — or asks to take something off the backlog. Prefer this over jumping straight into implementation, even when the issue looks obvious."
argument-hint: "<issue number> [more issue numbers]"
allowed-tools: Bash(gh *) Bash(git *) Bash(cp *) Bash(diff *) Read Grep Glob AskUserQuestion ExitPlanMode
---

# Ani-kickoff

Kicking off: $ARGUMENTS

The point of this skill is to spend thinking time where it is cheap. A wrong
assumption costs seconds to fix while it is still a sentence in a plan, and hours
once it is code with tests and a PR built on top of it. So the order here is
deliberately: understand → verify → analyze → ask → agree → *then* branch.

Nothing gets implemented during this skill. It ends at an approved plan and a
branch to build it on.

It does write to GitHub, in two narrow places, because findings that live only in
a chat window get re-derived at full price by the next person to open the issue:

- **Step 2** posts verified facts it observed — issue state, code that exists at a
  cited line, the commit that changed something. Facts only, never conclusions.
- **Step 6** corrects statements in the issue that the approved plan just made
  untrue.

Both are deliberate. What it never does unattended is publish a judgement — that
an issue is stale, or should be closed, or that one approach beats another. Those
stay in the conversation where they can be argued with.

---

## Step 1 — Load the issues

```bash
gh issue view <N> --json number,title,state,body,labels,createdAt,updatedAt,closedAt,comments \
  --template '#{{.number}} {{.title}} [{{.state}}] created={{.createdAt}} updated={{.updatedAt}} closed={{.closedAt}}{{"\n"}}labels:{{range .labels}} {{.name}}{{end}}{{"\n"}}--- body ---{{"\n"}}{{.body}}{{range .comments}}{{"\n"}}--- comment by {{.author.login}} @ {{.createdAt}} ---{{"\n"}}{{.body}}{{end}}'
```

That template deliberately renders every comment body, not just a count. Comments
are where the decision that nobody folded back into the body tends to live, and
they are disproportionately likely to be the thing that settles an issue: #56's
entire status is a one-line comment saying it was filed by mistake, and #52's
open design question exists only in its comment thread. A count tells you
nothing.

Issue bodies in this repo vary wildly in density, and that difference should
change how you work:

- **Sparse** (#112 is two sentences: "we should have some kind of setting or
  something") — nearly every requirement is unstated. Most of your effort belongs
  in Step 4, and you should expect to ask a lot.
- **Dense** (#63 has Background / Problem / Options with a recommended option and
  its tradeoffs) — the thinking is already on the page. Your job shifts to
  pressure-testing it rather than generating it from scratch. It is still a
  hypothesis, not a spec; see Step 3.

**Check `state` first.** A closed issue is the fastest possible staleness result,
and it happens — a batch like "let's do 14 and 56" can easily contain one that
shipped months ago. If it is `CLOSED`, say so immediately with the close date,
and ask whether they meant a different number or want to reopen the topic. Don't
plan work for it on the assumption they know.

---

## Step 2 — Verify the issue is still real

This is the step that earns the skill its keep. Issues in this repo go stale in
recognizable ways, so work through these deliberately rather than trusting the
issue's framing:

**Fetch first — your local `main` is probably stale.** Do this before any other
check in this step, because every `git log`, `git cat-file` and "does this file
exist yet" below reads from local refs, and a stale `main` makes all of them lie
in the same direction: work that has already merged looks unbuilt.

This is not hypothetical. Kicking off #141 and #111, local `main` sat at `925d79f`
while `origin/main` was at `377f9f9` — one merge ahead. The whole `AniListLinkTarget`
/ `BioLinkFollower` layer from #137 was on `origin/main` and invisible locally, so
an implementation option that reuses it was presented to the user as carrying a
"depends on an unmerged branch" cost it did not have. The user caught it. They
should not have had to.

```bash
git fetch origin --quiet
git log --oneline -1 main
git log --oneline -1 origin/main
git rev-list --count main..origin/main   # 0 = up to date
```

If `main` is behind, fast-forward it before going further — a clean tree makes
this free, and the branch you cut in Step 7 needs to come off the real tip anyway:

```bash
git merge-base --is-ancestor main origin/main   # confirm fast-forward is safe
git branch -f main origin/main                  # when not checked out on main
```

Two things this protects beyond the obvious:

- **Squash merges break `--contains`.** This repo squash-merges, so
  `git branch -r --contains <branch-sha>` returns nothing even for work that
  shipped. Don't conclude from an empty result that a branch is unmerged — check
  whether the *files* are on `origin/main` (`git cat-file -e origin/main:<path>`),
  or look for the squash commit by PR number.
- **Say which ref you checked.** When you report "X isn't implemented yet" or
  "Y doesn't exist on main", name the ref and SHA you verified against, so a stale
  local can be spotted by the user in one glance rather than after the plan is
  built on it.

**Search broadly before concluding anything.** This is the failure mode that will
burn you, so internalize it before the specific checks below: an empty search
result is not evidence of absence, it is usually evidence you guessed the wrong
word. Real examples from this repo — grepping `IsExpanded` found nothing and
nearly got shipped work reported as unbuilt, because the code says
`IsDescriptionExpanded` and `IsStatusExpanded`; `gh issue list --search "unit test PageModel
Services Converters"` returned one issue while `--search "unit test"` returned
three, including the one that mattered.

So: start with the shortest query that could plausibly match, then narrow. Try at
least two phrasings before believing a negative. When you do report something as
missing, say what you searched for, so the user can spot a wrong guess.

**Already fixed, wholly or partly.** Search for the symbols, files, or behavior
the issue names, and check history since it was filed:

```bash
git log --oneline --since=<issue createdAt> --grep=<keyword> -i
git log --format='%h %ad %s' --date=short -S "<code snippet>" -- <path>
gh pr list --state all --search "<keyword>" --limit 5 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
```

`git log -S` is the more reliable of the two log commands — it finds the commit
where a string actually entered or left the code, regardless of what the commit
message claimed.

Partial completion is the common case and the easy one to miss — half the issue
shipped, the remainder is still valid but the framing no longer matches reality.

**Superseded by a documented decision — but date it before you believe it.**
Docs that explain why the code is the way it is look like decisions to keep it
that way, and often aren't. Find when the documentation was written and compare
that to when the issue was filed:

```bash
git log --format='%h %ad %s' --date=short -S "<phrase from the doc>" -- AGENTS.md
```

The distinction that matters: documentation written *after* an issue, by someone
who knew about it, is a decision. Documentation written *alongside the very work
the issue is following up on* is just a description of the gap — which is what
the issue is already telling you.

#63 is the cautionary case. It asks to move `AiringCheckWorker` off
`Android.Util.Log`, and AGENTS.md explains that call site as an intentional
exception, which reads like the issue was overruled. It wasn't: the AGENTS.md
paragraph and the worker's inline comment both landed in `b96e96c` — PR #61, the
same PR whose unfinished scope #63 was filed to track, merged one day *after*
it. The doc is saying "here's why we haven't done this yet," not "we decided not
to." #63 is entirely live.

Treat this as the general lesson rather than a fact about #63: an explanation of
the status quo is not consent to it, and the timestamps will tell you which one
you are looking at.

**Duplicate or overlapping open issues.** Search the whole issue list, not just
open ones:

```bash
gh issue list --state all --search "<topic>" --limit 8 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
```

What you are looking for is not only exact duplicates but **dependency order**,
which is easier to miss and more expensive to get wrong. #62 ("Extract
AniSprinkles.Core class library") is the precondition for the highest-value
phase of #52 ("Add unit test suite for PageModels, Services, and Converters") —
planning #52 without noticing that means planning work that cannot start.

Report the relationship you actually found rather than rounding it to
"duplicate": *blocks*, *overlaps in part*, *supersedes*, and *duplicates* lead to
different recommendations.

**The premise moved.** The architecture may have changed underneath the issue.
#12's checklist asked for a "My Manga flyout page (parallel to My Anime)" — but
#43 replaced the hamburger flyout with a bottom tab bar, and the manga half now
exists as a Shell sub-tab placeholder. The work is still live; the shape it
described is not. Read `/project-architecture` and the relevant source before
accepting the issue's description of how things currently work.

Be careful not to over-apply this. #64 *looks* like the same case — an old perf
investigation, and the lists have moved around — but `MediaListLoadedContentView`
still forces `MeasureAllItems` at `xaml:395` and `xaml.cs:311`, so the
investigation is entirely valid. Only its citations had drifted. Check the code
before concluding a premise moved, exactly as you would before concluding an
issue is stale.

**The unstated assumption.** Most issues quietly assume some mechanism already
behaves a certain way, and the assumption is worth checking directly — it is
where the highest-value findings hide, precisely because nobody wrote it down to
be questioned. Pay particular attention to behavior that differs between Debug
and Release, since this repo diverges meaningfully across them (file-log level
and size, CI stub services, `AddDebug()` wiring).

#112 asks for a button to send diagnostic logs. The unstated assumption is that
the log file contains diagnostics — but Release persists only `Warning` and
above while every trace in the app (`NAVTRACE`, `PageState`, `CACHE`, `AUTH`) is
`Information`, so the shipped button would deliver a file missing exactly what
makes a report useful. That reframes the whole issue, and it is invisible unless
you go looking.

**External blockers.** For anything labeled `blocked`, or that depends on a
package or platform version, confirm the blocker's current status rather than
assuming — both that it is still blocking, and that it hasn't quietly resolved.

**Report before planning.** When you find staleness, stop and lay it out: what
specifically changed, the commit / PR / doc that changed it, how much of the
issue survives, and your recommendation (close it, narrow it, merge it with
another, or proceed as written). Then ask what they want to do. Cite evidence
rather than impressions — "this looks outdated" is not actionable; "PR #61
shipped the logger provider but not the worker migration, so the second half is
still valid" is.

**Record verified facts on the issue.** Findings die in the chat window otherwise,
and the next person to open the issue pays the same verification cost again. Post
them without asking — but only facts, and only with citations, because an
unattended write to a public issue is the one place a wrong claim does lasting
damage.

**Post automatically only what a command proved.** Each line must be checkable by
someone reading it, and carry the evidence that makes it falsifiable:

- issue state and dates (`#56 was closed 2026-04-16`)
- code that exists now, cited as `path:line`
- a commit or PR that changed something, cited by SHA or number
- another issue covering related scope, cited by number — state the overlap you
  observed; *blocks* and *supersedes* are conclusions rather than observations,
  so they stay in the chat

**Never post automatically:** that an issue is stale, that it should be closed or
narrowed, which approach is better, or anything else you concluded rather than
observed. Those belong in the chat, where the user can correct them — and they
do get corrected. A first reading of #63 that AGENTS.md had overruled it looked
solid and was wrong; only comparing timestamps showed why. Had that been posted
unattended, a false claim would be sitting on the issue with no one to catch it.

**Update your previous comment instead of adding another.** Kickoff runs repeat,
and an issue accumulating near-identical findings comments is worse than no
findings at all. Mark the comment and look for that marker first — the same
pattern `ci-build-and-preview.yml` uses for screenshots:

```bash
# Find a previous findings comment (numeric id, or empty if this is the first).
# --paginate is load-bearing: the endpoint returns 30 comments per page by
# default, so on a busy issue an unpaginated lookup misses the marker and
# silently posts a duplicate instead of updating.
gh api --paginate 'repos/<owner>/<repo>/issues/<N>/comments?per_page=100' \
  --jq '.[] | select(.body | contains("<!-- ani-kickoff-findings -->")) | .id'

# Update it in place
gh api repos/<owner>/<repo>/issues/comments/<id> -X PATCH -f body='<!-- ani-kickoff-findings -->
...'

# Or, if none exists, create it
gh issue comment <N> --body '<!-- ani-kickoff-findings -->
...'
```

Write it in the user's own voice as plain repo notes — no agent branding, per
`AGENTS.md`. One consolidated comment per issue, not one per finding. If
verification turned up nothing worth persisting, post nothing; silence is a
perfectly good result.

---

## Step 3 — Work out the right solution

If the issue proposes a solution, treat it as a well-informed hypothesis from
someone who had context you may lack — worth taking seriously, not worth
adopting unexamined. It was written before the code was, and the codebase has
opinions.

Come up with at least one genuine alternative before settling. If the issue's
option really is best, saying *why* it beat the alternative is far more useful
than saying it was the only thing considered.

Check candidate approaches against the conventions that actually bite here:

- **DI lifetimes** — services and tab PageModels are singleton; pages and
  details PageModels are transient. Getting this wrong produces state that leaks
  across navigations or is silently discarded. See `/project-architecture`.
- **Navigation** — routes plus lightweight query params, never full objects.
- **Rate-limit budget** — new AniList reads are expensive. Batch with GraphQL
  aliases where you can, and be honest in the plan about how many requests an
  approach adds. See the AniList section of `AGENTS.md`.
- **CI fixture counterpart** — any new `IAniListClient` operation, new sort, or
  deeper page needs a recording, or the CI screenshot job fails with
  `FIXTURE MISS` (#134). Re-record with `tools/record-anilist-fixtures.cs`. Better
  than the old hand-written stub, which answered anything it did not model with an
  empty list and failed silently — but still a step that is easy to forget until
  the run goes red.
- **Zero warnings** — the build must stay clean.
- **Testability** — `tests/` project-references `src/AniSprinkles.Core/`, so
  anything in Core is testable and anything in the MAUI app project is not. If an
  approach puts logic on the app side of that line, that is a real tradeoff to
  name. Watch the off-device asymmetry too: `Shell.Current` and
  `Application.Current` are `null` there (a silent no-op that passes as a test),
  while `Preferences`, `MainThread`, `AppInfo` and `Browser` throw. Reaching any
  of them from a page model instead of an injected seam makes it untestable.

Name the tradeoffs plainly, including the ones that argue against your own
recommendation. Flag anything that is hard to reverse, touches auth or tokens,
or changes on-device behavior you cannot verify from the desktop.

---

## Step 4 — Ask clarifying questions

The user's explicit goal is to fix it right the first time, which means questions
are welcome — but their value comes from being answerable and consequential, not
from their quantity.

Ask about anything where two readings lead to genuinely different code. Skip
anything you can settle yourself by reading the codebase; burning a question on
something greppable spends the user's attention badly and makes the real
questions easier to skim past.

Good questions tend to be grounded in something specific you found: "the issue
says diagnostic logs should be sendable — the file logger keeps 3 rotated
archives at `{AppDataDirectory}/logs/`; should the share include all of them or
just the current one?" beats "how should logging work?"

Worth probing on most issues:

- Scope edges — what is explicitly *not* in this change
- What "done" looks like, concretely enough to verify on device
- Whether it needs a settings toggle, or is always-on
- Existing behavior that may be relied on and shouldn't change
- Whether it should work signed-out (much of the app does)

Use `AskUserQuestion` for these rather than listing them in prose — it is the
project convention, and it makes them far easier to answer in one pass. Batch
related questions into a single call instead of trickling them out one at a
time. If a question has an obvious sensible default, offer it as the recommended
option so answering is cheap.

---

## Step 5 — Present the plan and get approval

Present the plan in the conversation and end at an explicit approval gate
(`ExitPlanMode`) so nothing gets built before the user has agreed. Include:

- **What we're solving** — restated in your own words, reflecting what you
  verified in Step 2, not just the issue's title
- **Approach chosen, and what it beat** — with the reasoning
- **Steps** — ordered, each concrete enough to act on, naming the files involved
- **Tradeoffs and risks** — what could go wrong, what is hard to undo
- **Verification** — how we'll confirm it works. For anything user-visible this
  means the real app via `/run-anisprinkles`, since unit tests here cover pure
  algorithms and can't tell you a screen renders correctly
- **Out of scope** — what we're deliberately not doing

If the answers in Step 4 changed your thinking, say so and say how. The user
should be able to see their input reflected rather than having to check.

---

## Step 6 — Correct what the plan just made untrue

Planning frequently supersedes something the issue states outright. If the issue
says four tabs and you agreed on five, the issue is now wrong, and it will stay
wrong until someone rediscovers it mid-implementation. Fixing it costs seconds
here and is the cheapest moment it will ever cost.

Compare the approved plan against the issue's **specific claims** — acceptance
criteria, checklists, counts, named files, proposed APIs — and change only what
the plan actually contradicts. This is narrow work. You are not rewriting the
issue, summarising the plan into it, or tidying its prose; you are correcting
statements that stopped being true in the last ten minutes.

Where the issue states a spec (checkboxes, acceptance criteria, an options list),
edit the body so future readers see current intent rather than a stale spec they
have to mentally patch.

`gh issue edit --body-file` **replaces the entire body**, so never compose a
replacement from scratch — fetch what is there, change the contradicted claims in
place, and write the whole thing back. Composing it fresh is how a narrow
correction turns into silently deleting everything else the issue said.

Keep the working files in a scratch directory outside the repo (`$SCRATCH` below
— your session scratchpad, or any temp dir), so a stray issue body can never end
up in a commit:

```bash
# 1. Fetch the current body, and keep a pristine copy to diff against.
gh issue view <N> --json body --jq .body > "$SCRATCH/issue-<N>-body.md"
cp "$SCRATCH/issue-<N>-body.md" "$SCRATCH/issue-<N>-body.orig.md"

# 2. Edit issue-<N>-body.md, changing only the claims the plan contradicts.
#    Everything else must survive the round trip byte for byte.

# 3. Confirm the diff contains only deliberate changes.
diff "$SCRATCH/issue-<N>-body.orig.md" "$SCRATCH/issue-<N>-body.md"

# 4. Write the complete, modified body back.
gh issue edit <N> --body-file "$SCRATCH/issue-<N>-body.md"
```

If step 3 shows anything you did not deliberately change, do not run step 4.

Then leave a short comment recording what changed and why. The edit keeps the
issue truthful; the comment keeps the history, so nobody wonders whether the
original said something different:

```
Updated during planning: tab count 4 → 5. The fifth is needed because <reason>.
```

This is safe to do without asking, because you are recording a decision the user
just approved rather than one you reached on your own. That is the distinction —
you may write down what was decided, not what you concluded. If the plan
contradicts nothing the issue states, change nothing.

---

## Step 7 — Cut the branch

Only after approval. Branch off `origin/main` — not local `main`, unless Step 2's
fetch confirmed the two are level — named `feature/<issue#>-<short-slug>`
(e.g. `feature/112-diagnostic-log-export`) — that convention is the most useful
of the several in this repo's history because it ties the branch back to the
issue. For a batch that is genuinely one unit of work, use the lowest issue
number and mention the others in the branch name or the eventual PR body.

Confirm the branch name before creating it; it is annoying to rename later.

Create the branch and stop there. Don't commit, don't push, and don't start
implementing — per `AGENTS.md`, those are separate explicit asks. Hand back with
a one-line summary of what was agreed and what the next step is.

---

## Handling several issues at once

When given multiple numbers, work out whether they are one unit of work or
separate threads before planning anything — the answer changes everything
downstream.

They are **one unit** when they share a root cause, when one is a precondition
for another (#62 unblocks #52), or when doing them separately would mean touching
the same code twice. Plan them together, one branch, and be explicit about the
ordering between them.

They are **separate** when they merely share a page or a label. Say so, and ask
whether to take them one at a time or plan both now — planning several unrelated
issues in one pass produces a plan too big to hold in your head, and the
clarifying questions bleed together confusingly.

Either way, run Step 2 on each issue independently. Staleness is per-issue, and a
batch is exactly where a closed or superseded one hides.

---

## Related skills

- `/project-architecture` — DI table, page patterns, navigation, performance defaults
- `/ani-debug` — on-device diagnostics, when the issue is a bug observed on a device
- `/run-anisprinkles` — build, launch, and drive the app to reproduce a bug or verify a fix
- `/ani-review` — the review pass to run after the implementation lands
