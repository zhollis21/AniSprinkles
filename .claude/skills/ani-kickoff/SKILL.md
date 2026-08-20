---
name: ani-kickoff
description: "Start work on a GitHub issue the right way: pull the issue, verify it isn't stale or already fixed, analyze solutions independently of whatever the issue proposes, surface tradeoffs, ask clarifying questions, and agree a plan before any code exists. Use whenever the user names issue numbers to work on — \"let's do #112\", \"start issue 63\", \"pick up 52 and 62\" — or asks to take something off the backlog. Prefer this over jumping straight into implementation, even when the issue looks obvious."
argument-hint: "<issue number> [more issue numbers]"
allowed-tools: Bash(gh *) Bash(git *) Read Grep Glob AskUserQuestion ExitPlanMode
---

# Ani-kickoff

Kicking off: $ARGUMENTS

The point of this skill is to spend thinking time where it is cheap. A wrong
assumption costs seconds to fix while it is still a sentence in a plan, and hours
once it is code with tests and a PR built on top of it. So the order here is
deliberately: understand → verify → analyze → ask → agree → *then* branch.

Nothing gets implemented during this skill. It ends at an approved plan and a
branch to build it on.

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

**Search broadly before concluding anything.** This is the failure mode that will
burn you, so internalize it before the specific checks below: an empty search
result is not evidence of absence, it is usually evidence you guessed the wrong
word. Real examples from this repo — grepping `IsExpanded` found nothing and
nearly got shipped work reported as unbuilt, because the code says
`IsDisplayPreferencesExpanded`; `gh issue list --search "unit test PageModel
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
#64 proposes investigating `ItemSizingStrategy` for CollectionView perf, but
those lists were since migrated from `BindableLayout` to virtualized
`CollectionView`s, which is most of what that investigation was chasing. Read
`/project-architecture` and the relevant source before accepting the issue's
description of how things currently work.

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

- **DI lifetimes** — services and flyout PageModels are singleton; pages and
  details PageModels are transient. Getting this wrong produces state that leaks
  across navigations or is silently discarded. See `/project-architecture`.
- **Navigation** — routes plus lightweight query params, never full objects.
- **Rate-limit budget** — new AniList reads are expensive. Batch with GraphQL
  aliases where you can, and be honest in the plan about how many requests an
  approach adds. See the AniList section of `AGENTS.md`.
- **CI stub counterpart** — any new `IAniListClient` operation needs its
  `CIAniListClient` twin, or the CI screenshot job breaks. Easy to forget, and it
  fails well after the fact.
- **Zero warnings** — the build must stay clean.
- **Testability** — `tests/` link-compiles individual files from `src/` rather
  than referencing the MAUI app, so what is practically testable is constrained.
  If an approach is untestable under that setup, that is a real tradeoff to name.

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

## Step 6 — Cut the branch

Only after approval. Branch off `main`, named `feature/<issue#>-<short-slug>`
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
