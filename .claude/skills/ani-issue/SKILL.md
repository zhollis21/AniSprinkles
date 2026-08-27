---
name: ani-issue
description: "File a GitHub issue that survives contact with the future: interrogate the user for what only they know, verify every claim against the actual code before writing it down, state the problem rather than decree a fix, label it, and create it. Also audits existing issues against the same bar (`audit <N>` / `audit all`). Use whenever the user wants to file, open, raise, log, or write up an issue, ticket, bug report or piece of tech debt — and whenever a session turns up a problem that isn't going to get fixed right now, even if they didn't use the word 'issue'. Prefer this over calling `gh issue create` directly, always."
argument-hint: "<what's wrong> | audit <issue number|all>"
allowed-tools: Bash(gh *) Bash(git *) Read Grep Glob AskUserQuestion
---

# Ani-issue

Request: $ARGUMENTS

An issue in this repo is not a reminder. It is a message to someone — usually the
same person, months later — who has lost all the context that made the problem
obvious. `/ani-kickoff` will pick it up cold and has to be able to trust it.

That sets the bar: **every factual claim in the body must be one a reader can
check**, and every claim you write must be one *you* checked first. A cited
`file.cs:119` that drifted two refactors ago is worse than no citation, because
it costs the next reader time before it costs them trust.

Two things this deliberately does *not* do:

- **It doesn't decide the fix.** Design happens in `/ani-kickoff`, against the
  code, with the user present. An issue that arrives as a spec quietly skips that
  step. Options with tradeoffs, yes — a decree, no. See *Stating the problem*.
- **It doesn't file on a hunch.** If the dig contradicts the premise, it stops and
  says so rather than adding a wrong issue to the backlog.

It **does** file without a final approval prompt. The interrogation *is* the
approval gate — which means the interrogation has to be good enough that the user
never wishes they'd had one more look. Two exceptions halt it anyway, both in
Step 3.

Modes: **create** (default) and **audit** (`audit 63`, `audit all`) — see the
last section.

---

## Step 1 — Take stock before asking anything

Questions the user already answered are the fastest way to spend their patience.
Start by working out what you actually have, because the entry points differ
enormously in how much is already known:

- **Mid-session discovery** — you hit this while doing something else, and the
  conversation is full of evidence. Mine the transcript first: the file you were
  reading, the failing test output, the thing the user said was wrong. Most of the
  body is already sitting in the session. Expect one round of questions.
- **Cold one-liner** — `/ani-issue the sort popup resets when you pick a sort`.
  Nothing but a sentence. The repo dig in Step 2 does the heavy lifting, and
  expect two or three rounds.
- **After `/ani-debug` or `/run-anisprinkles`** — logcat, `PageState` transitions,
  `NAVTRACE`, screenshots. This is the strongest evidence the repo gets, because
  it's observed rather than inferred. Pull the excerpts in verbatim (see *Device
  evidence*).
- **One finding, several problems** — see Step 4 before grilling anything.

Write down, for yourself, the list of things you don't yet know. That list is what
Step 5 asks about, and nothing else.

---

## Step 2 — Dig, and verify every claim you intend to make

The user reports a symptom. The issue needs a mechanism, and the mechanism lives
in the code. This step is what makes the difference between "Enable Diagnostic
Logging" (#112 — two sentences, and `/ani-kickoff` had to discover on its own that
Release logging drops everything that makes a report useful) and #125, which names
the file, the line, and the `#if` that causes the problem.

**An empty search result is not evidence of absence.** It is usually evidence you
guessed the wrong word — grepping `IsExpanded` in this repo finds nothing, because
the code says `IsDescriptionExpanded` and `IsStatusExpanded`. Try at least two phrasings before
believing a negative, and if you end up writing "there is no X" in the body, say
what you searched for so a wrong guess is visible.

Work through as much of this as the claim requires:

```bash
# The mechanism: find the symbol, then read the surrounding code, then cite.
grep -rn "<symbol>" src/ tests/

# When it broke, and what it looked like before. -S is the reliable one: it finds
# the commit where a string entered or left the code, whatever the message said.
git log --format='%h %ad %s' --date=short -S "<code snippet>" -- <path>
git log --oneline -n 5 -- <path>

# Prior art — open AND closed, because "we tried that" lives in closed issues.
gh issue list --state all --search "<topic>" --limit 8 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
gh pr list --state all --search "<keyword>" --limit 5 --json number,title,state \
  --template '{{range .}}#{{.number}} [{{.state}}] {{.title}}{{"\n"}}{{end}}'
```

Then, before any line number goes in the body, **open the file and confirm the
code is at that line right now.** Cite `path:line` only for something you just
read. If you're citing a range that might shift, quote the snippet in a fenced
block as well — a quoted snippet stays checkable even after the line moves, which
is why #125 and #134 both do it.

Three things drift, and only the first is obvious:

- **Line numbers**, constantly.
- **Paths.** #62 moved everything under `src/AniSprinkles/` and `src/AniSprinkles.Core/` in
  `490fdd5`. Any citation starting `src/` with neither of those next is from before that
  and is wrong — it broke #64, #68 and #111 simultaneously, and none of them
  looked wrong on the page. Treat a bare `src/Views/…` or `src/Platforms/…` as a
  red flag on sight.
- **Method names.** #64 cited a method called `OnListViewModeChanged`; the code
  says `ApplyViewMode`. A name that no longer exists is worse than a stale line
  number, because grepping for it returns nothing and the next reader concludes
  the code was deleted.

The quoted snippet is what survives all three. Prefer it.

Check the conventions that make problems in this repo different from problems in
general — `/project-architecture` and `AGENTS.md` are the reference, and these are
the ones that most often turn out to *be* the mechanism:

- **DI lifetimes** — services and tab PageModels singleton; pages and details
  PageModels transient. State leaking across navigations or vanishing between them
  is nearly always this.
- **Debug vs Release vs `-p:CiBuild=true`** — this repo genuinely diverges: file-log
  level and size, CI stub services, `AddDebug()` wiring. A bug that only reproduces
  in one configuration is a fact about the issue, not a detail. Always record which
  configuration the observation came from.
- **CI stubs** — `CIAniListClient` fakes a lot, and what it fakes badly is
  invisible to the screenshot job (that is the entire subject of #134). If the
  problem touches an `IAniListClient` call, check whether the stub can even
  express it.
- **The test boundary** — `tests/` project-references `src/AniSprinkles.Core/`
  only. Anything in the MAUI app project is untestable off-device. If the problem
  lives on the wrong side of that line, that is part of the problem.
- **Rate-limit budget** — new AniList reads are expensive, and the user is wary of
  429s. Anything proposing more requests must say how many.

---

## Step 3 — Stop if the dig undercuts the premise

Two findings are worth more than a filed issue, and both halt the run. Show the
evidence, then ask what the user wants to do — don't decide for them.

**It's already fixed, or a duplicate.** Show the commit, PR, or existing issue,
and offer the real choices: file anyway (the overlap is partial), add the new
evidence as a comment on the existing issue, or drop it. Say which relationship
you actually observed — *duplicates*, *overlaps in part*, *blocks*, *supersedes* —
they lead to different answers, and rounding them all to "duplicate" throws away
the useful part. Dependency order is the easy one to miss: #62 was the
precondition for the valuable half of #52 — and nothing in either title said so.

**The fix is smaller than the issue describing it.** If Step 2 landed on a
one-line change with an obvious shape and no design question, say so and offer to
just do it. A backlog earns its usefulness by being mostly things that genuinely
need thinking about; a two-minute fix filed as an issue costs more to triage twice
than to fix once. The user may still want it filed — a fix they can't verify today,
or one that belongs to a batch — and that's a fine answer. Ask rather than assume.

Nothing else halts the run. A thin issue, an uncertain priority, a problem you
can't fully explain — those are things to grill about, not reasons to stop.

---

## Step 4 — If it's several problems, agree the split first

One investigation often surfaces several genuinely separate problems, and the
split is a judgement the user should make before anyone spends questions on the
pieces. Getting it wrong in either direction is expensive: three issues that are
really one produce three half-plans, and one issue that is really three never gets
closed.

Show the proposed split as titles plus a one-line scope each, and let the user
merge, drop, or reshape. The test for "separate" is whether each could be fixed,
verified, and closed on its own — not whether they were found together. #134 is
the model: two CI-stub gaps found while implementing #125 and #130, filed as one
issue because they are one fix in one file, with the two others cross-linked.

Then grill each surviving issue in turn — scope, priority and constraints differ
per issue, and a shared interrogation flattens exactly the details that matter.
Cross-link them in `## Related` once the numbers exist.

If it's genuinely one problem, skip this entirely.

---

## Step 5 — Grill

Use `AskUserQuestion`, batching up to four questions per call rather than
trickling them out — it's the project convention and it's far cheaper to answer
in one pass. Ask only what's on the Step 1 unknowns list.

**Never ask what you could have grepped.** A question spent on something in the
code spends the user's attention badly, and makes the real questions easier to
skim past. Ground each question in what the dig found:
*"`DetailsPageModelBase.cs:276` deliberately keeps the existing sort when the same
entity reloads, so a sort-popup dismiss can't discard it — should returning from a
pushed sub-page behave the same way, or reset?"* beats *"how should sorting
work?"* by a mile.

Offer a recommended option where there's a sensible default, so cheap questions
stay cheap.

The dimensions worth probing, in rough order of how often they turn out to
matter — take what applies and drop the rest:

- **Expected vs actual.** What should have happened. Surprisingly often unstated,
  and it's the whole difference between a bug and a preference.
- **Reproduction.** Exact steps, build configuration, signed-in or out, which
  screen. If it was seen on device, when — dated observations are what let a
  future reader tell a live bug from a fixed one.
- **Frequency and blast radius.** Every time or once; one screen or a pattern
  repeated across four details pages. This is usually what sets priority.
- **Scope edges.** What is explicitly *not* part of this. The cheapest sentence in
  any issue.
- **Constraints that must hold.** Contracts a fix can't break — the adult-content
  canary, the CI screenshot job, the rate-limit budget. #134 has a whole section
  for exactly this reason.
- **Signed-out behaviour.** Much of this app works signed-out, and it's routinely
  forgotten.
- **Settings toggle or always-on**, if it's a behaviour change.
- **Priority** — always. See Step 7.

Stop when the remaining unknowns wouldn't change what a future reader does. Depth
should scale with what you don't know, not with ceremony: a well-evidenced
mid-session bug may need one round, a cold one-liner three.

---

## Step 6 — Compose the body

### Title

A title is the only part most readers see. Make it a **statement of what is
wrong**, not a topic:

- `Staff Name Language setting is saved to AniList but never read`
- `Pushed pages never run OnDisappearing on a tab switch, so in-flight loads are not cancelled`
- `CI stubs: details-page paging and userPreferred are too static to catch regressions`

An area prefix (`CI stubs:`, `My Anime page:`, `Voice Roles section:`) earns its
place when it scopes; a type prefix (`bug:`, `feat:`) does not — that's what the
label is for, and the older issues carrying them predate the label set. Compare
`Enable Diagnostic Logging` (#112) against the three above: it names a topic, and
you have to open it to learn there's a problem at all.

### Spine

Two sections are always there. The rest appear only when they have real content —
an empty `## Constraint` is worse than no `## Constraint`, because it teaches the
reader to skim headings.

**Always:**

- **An opening line or two of context** — where this was found and what was being
  done. #134 opens with "Both found while implementing #125/#130," which is how a
  future reader dates it instantly.
- **`## Problem`** — the mechanism, with the verified evidence inline: cited
  `path:line`, fenced snippets of the actual code, observed behaviour with dates
  and build flags. If there are several distinct facets, number them (`## 1. …`)
  as #134 does.

**When they have content:**

- **`## Impact`** (or a more specific heading like #125's *What this makes
  untestable today*) — what this costs, concretely. Vague severity claims are
  worth nothing; "the retry path has never run on a device" is worth a lot.
- **`## Options`** — see below.
- **`## Constraint`** — contracts a fix must not break.
- **`## Related`** — other issues, with the relationship stated: *"#130 — where the
  `userPreferred` gap was introduced."* A bare number makes the next reader do the
  work again.

### Stating the problem, not the fix

This is the part most worth getting right, and the line is finer than
"never suggest anything."

Thinking you had at filing time is genuinely valuable — throwing it away means
paying for it twice. What's harmful is thinking that arrives looking like a
decision, because `/ani-kickoff`'s entire job is to design against the real code,
and an issue phrased as a spec makes skipping that step the path of least
resistance.

So: **if you have fix ideas, present them as options with tradeoffs, and mark any
leading candidate as a hypothesis.** Explicitly, in the text — the marking is what
does the work.

```markdown
## Options

Not settled — these are the shapes the fix could take, recorded while the context
was fresh. Tradeoffs are the point; pick at kickoff time against the real code.

**A. Decorate the client.** Wraps whichever `IAniListClient` is registered, so
faults compose with the CI fixtures. Reaches the retry path. Doesn't reach
anything below `IAniListClient` — the rate-limit handler, the classifier.

**B. Inject at the HTTP layer.** Exercises the whole pipeline including
`AniListRateLimitHandler`. More setup, and harder to target one call.

Leaning A first — it's the smaller change and unblocks the retry-path gap, which
is the concrete thing that's untestable today. That's a starting point for
kickoff, not a decision.
```

What that avoids is `## Proposal` and `**Fix:** …`, which read as settled — even
when the author meant them as suggestions, which is exactly what happened in #125
and #134. Same content, different contract with the reader.

Two shapes stay out of the body entirely: acceptance-criteria checklists and
named APIs presented as the design. Those are plan artifacts, and an issue holding
a stale spec is worse than an issue holding none.

### Device evidence

When the observation came from a device or emulator run, that's the strongest
material available — record it so it stays checkable:

````markdown
**Observed on device** 2026-08-25 with `-p:CiBuild=true`: on ONE PIECE (media 21),
switching the Characters sort from "Role" to "Most Favorited" made the entire
Characters section disappear.

```
PageState: MediaDetails Loading -> Loaded (412ms)
NAVTRACE: push MediaDetailsPage id=21
```
````

Always include the date and the build configuration. `gh` can't upload images, so
if a screenshot is what makes the problem legible, say so in the body — *"a
screenshot of the empty section is worth attaching here"* — and give the user the
local file path so they can drag it in. Don't commit screenshots to the repo to
work around this.

### Voice

Write it as the user's own repo notes. No agent branding, no "I found", no
attribution footers — `AGENTS.md` line 185 covers issue bodies explicitly, and the
existing issues are all in first-person-plural repo voice.

---

## Step 7 — Labels

Propose the full set with a one-line justification each, and confirm via
`AskUserQuestion` in the same round as the last content questions where possible.
The type label is usually clear from the body; the priority is a judgement only
the user can make, so it's always asked.

| Label | Use for |
|---|---|
| `bug` | Behaviour that is wrong against a stated or obvious expectation |
| `enhancement` | Improvement to something that already exists |
| `feature` | New functionality that doesn't exist yet |
| `Tech Debt` | Cleanup, refactoring, test coverage, architectural improvement |
| `UI/UX` | Touches what the user sees or how it feels — stacks with the above |
| `documentation` | Docs only |
| `epic` | Tracking issue for work spanning several issues |
| `blocked` | Waiting on an external dependency or upstream fix |
| `p1-critical` … `p4-low` | Priority — exactly one |

Multiple type labels are normal and correct: #125 is `enhancement` +
`Tech Debt` + `p3-medium`.

**Every issue gets a priority.** Seven open issues currently have none, which
makes the backlog unsortable — don't add to that. If the user genuinely can't
call it, `p4-low` with a note beats silence, because an explicit low is a decision
and an absent label is a gap.

If nothing in the set fits the kind of work — not merely a narrower flavour of an
existing label, but a genuinely uncovered category — propose creating one: name,
description, colour, and why the existing 18 don't cover it. Wait for an explicit
yes; a label set grows once and gets pruned never, so the bar is high.

```bash
gh label create "<name>" --description "<description>" --color "<hex>"
```

---

## Step 8 — File it

Compose the body in a scratch file and pass it with `--body-file`. This is not
style: `#68`'s body is visibly corrupted — every backtick in it became `\`,
because the body went through PowerShell inline quoting. A file round-trip has no
such failure mode.

The same reasoning applies to *editing* the file once it exists. Issue bodies are
full of backticks, backslashes, quotes and `$`, which is precisely the character
set that shell quoting mangles. `sed`/`perl` one-liners on this content fail in
ways that look like your regex is wrong when the shell already ate it — expect to
burn several attempts and, worse, to half-apply a multi-expression script. Use
Read + Edit with literal strings for anything containing backslashes or nested
quotes, and keep `sed` for plain-text substitutions where you can see every
character is safe.

Keep the file outside the repo (`$SCRATCH` — the session scratchpad or any temp
dir), so a stray issue body can never end up in a commit:

```bash
# Write the body to "$SCRATCH/issue-body.md" first, then:
gh issue create \
  --title "<title>" \
  --body-file "$SCRATCH/issue-body.md" \
  --label "<type>" --label "<priority>"
```

Then read it back and look at it, because a rendering problem is invisible in the
source and permanent in the issue:

```bash
gh issue view <N> | head -40
```

Report the URL and the labels set. If the body came out wrong, fix it with
`gh issue edit <N> --body-file` immediately — before the user has to notice.

For a split from Step 4, file all of them, then edit the `## Related` sections to
cross-link the real numbers once they exist.

---

## Audit mode

`audit <N>`, `audit <N> <M>`, or `audit all` (all open issues, oldest first —
that's where the rot is). This applies the same bar above to issues that already
exist, and doubles as the honest test of whether the bar is any good.

**Nothing is written without approval.** Creating a wrong issue wastes triage;
editing a real one destroys history that nobody kept a copy of. Report findings,
per issue, and let the user decide each — including doing nothing, which is very
often right for an old issue that's thin but still true.

Load the issue with its comments (the template in `/ani-kickoff` Step 1 renders
comment bodies, which matters — decisions in this repo routinely live only in a
comment and were never folded into the body). Then check:

1. **Is the problem still real?** Run the Step 2 dig against today's code. Partial
   completion is the common case and the easy one to miss: half shipped, the rest
   still valid, framing no longer matching reality.
2. **Are the claims still true?** Cited `path:line` drifts. Verify each one and
   note which moved.
3. **Is the body intact?** Shell-quoting damage is common here and takes more
   forms than you would guess. The ones this backlog actually contained:

   | Looks like | Was | Seen in |
   |---|---|---|
   | `\NotificationHelper\` | backticks became `\` | #68 |
   | `^Gspire-apphost` | the literal two characters `^` `G` | #36 |
   | `\x08uilder.Add…` | a real backspace byte from `` `b `` | #36 |
   | `\x0dun arg` | a real CR from `` `r `` | #36 |
   | `ItemSizingStrategy=\"…\"` | escaped quotes | #64 |
   | `Discover''s` | doubled apostrophe | #101 |

   **`cat -v` will lie to you.** It renders both a real BEL byte and the literal
   text `^G` identically. Confirm with `od -c` before writing a substitution, or
   you will spend three rounds fixing a control character that was never there.
   Repair with Read + Edit, per Step 8.
4. **Labels.** Missing priority, missing type, a `blocked` that may have quietly
   unblocked — and any body that *states* its own priority. #90's `## Priority`
   section said `p3-medium` while its label said `p4-low`. Labels are what the
   backlog sorts by, so a body that repeats a priority is a contradiction waiting
   to happen: drop the section rather than reconciling it.
5. **Named blockers that have since closed.** Distinct from "already fixed", and
   easy to miss because the issue still looks accurate — it is only the *deferral*
   that went stale. #52 held three items back on blockers that had all closed
   (#119, #120, #121); #112 named #124 as a hard dependency, also closed. Any
   sentence containing "blocked", "needs X first", or "once #N lands" is a
   `gh issue view` away from being checked, and clearing one can unblock real work.
6. **Title.** Does it state the problem or just name a topic? Does it carry a
   redundant `bug:`/`feat:` prefix?
7. **Is a fix presented as settled?** `## Proposal` and `**Fix:**` sections written
   before the code was read. Flag for reframing as options — noting that the
   reasoning is worth keeping, it's the framing that needs to change.
8. **Decisions stranded in comments.** If a comment settles something the body
   still contradicts, the body is actively misleading. This is usually the
   highest-value finding an audit produces.

Present per issue: what you verified, what's wrong, and a specific recommended
action — *edit the body*, *add labels*, *close as fixed by `<sha>`*, *merge into
#N*, *leave it*. Then act only on what the user approves.

When editing an approved body, `gh issue edit --body-file` **replaces the whole
body** — so fetch, edit in place, diff, and only then write back. Composing a
replacement from scratch is how a narrow correction turns into silently deleting
everything else the issue said:

```bash
gh issue view <N> --json body --jq .body > "$SCRATCH/issue-<N>.md"
cp "$SCRATCH/issue-<N>.md" "$SCRATCH/issue-<N>.orig.md"
# edit issue-<N>.md, then:
diff "$SCRATCH/issue-<N>.orig.md" "$SCRATCH/issue-<N>.md"
gh issue edit <N> --body-file "$SCRATCH/issue-<N>.md"
```

If the diff shows anything you didn't deliberately change, don't write it back.

For `audit all`, work oldest-first and report in batches rather than one wall of
findings — 26 open issues is more than anyone triages in one sitting.
