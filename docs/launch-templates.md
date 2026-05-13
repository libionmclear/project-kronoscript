# Launch & support email templates

Pre-staged copy for the moments when you don't want to write under pressure.
Edit the placeholders ({{LIKE_THIS}}) before sending.

---

## 1. Beta invite — first batch

**Subject:** You're invited to the Kronoscript beta — write down what you don't want to forget

Hey {{FIRST_NAME}},

I've been quietly building Kronoscript for the last few months — a place to write down
the memories that made you, share them with the people who lived them with you, and
keep a record of your life that won't disappear into a Facebook scroll.

It's an early beta. I'm inviting a small group of people I know to try it before I
open it up more broadly. You're one of them.

What it is: a quiet, ad-free social network for your history. Write a quick story or
a long one, attach a photo, set who can see it (Public, Friends, Family, or Private),
and tag the people who were there. Their copy of the memory shows up on their timeline
too. Over time, what you build becomes a chronological record of your life — exportable
to a document at any time.

What it isn't: another timesink. There's no algorithm pushing strangers into your feed,
no push notifications hounding you, no ads. You write when you have something to say.

Here's your invite link: {{INVITE_URL}}

Three things to know up front:

1. It's beta. Things may break. If something does, please email me directly — I read
   every message and usually fix things within a day.
2. Don't post confidential information (passwords, financial data, etc.). The data is
   encrypted and we don't share or sell it, but no online platform is a vault.
3. Your stories belong to you. There's an Export feature; if you ever leave, you take
   everything with you.

I'd love to hear what you think — feedback shapes where this goes.

Thanks for being one of the first.

— {{YOUR_NAME}}
{{YOUR_EMAIL}}

---

## 2. "We hit a bug" — apology / status update

**Subject:** Quick update on a Kronoscript hiccup

Hey,

I noticed (or you noticed and reported — thank you) that {{WHAT_BROKE}} hasn't been
working since {{WHEN_IT_STARTED}}. I'm sorry about that — beta software, beta
problems.

Here's where we are:

- Status: {{INVESTIGATING / FIXED / DEPLOYED / STILL_WORKING_ON_IT}}
- Cause: {{ONE_LINE_PLAIN_LANGUAGE — e.g. "a recent change to the comment translation
   feature didn't account for posts with no body text, which threw a server error"}}
- Impact: {{WHO_WAS_AFFECTED — e.g. "anyone clicking Translate on a particular type
   of post"}}
- What I'm doing: {{ROLLED_BACK / DEPLOYING_FIX / ADDED_A_TEST}}

If your data was affected (it usually isn't — server-side errors don't typically
delete or corrupt content), I'll follow up individually.

You don't need to do anything; the fix doesn't require any action on your side.

If you ran into anything weird while this was happening — your feedback genuinely
helps. Just hit reply.

Thanks for your patience while I keep building.

— {{YOUR_NAME}}

---

## 3. Welcome — first sign-in (optional manual outreach)

Send to people who actually completed signup, ~24 hours after they joined.

**Subject:** Welcome to Kronoscript — three things that might help

Hey {{FIRST_NAME}},

Glad you're in.

A few quick things that aren't obvious from the interface but help a lot:

1. **Quick Story vs Full Story.** Quick Story is for one-line memories you don't want
   to lose ("the dog I had as a kid was named Rex"). Full Story is for the longer ones
   with a date, photos, and the people who were there. Both end up on your timeline.

2. **Visibility tiers matter.** Each post has a setting — Public, Friends, Family,
   Acquaintances, or just-me. The matching tier in your Network controls who sees
   what. So a "Family-only" post is invisible to your Friends-tier connections.

3. **Tag the people who were there.** When you tag someone in a memory, it appears on
   their timeline too — they can read your version, write theirs, and you both end up
   with a richer record.

The {{TOUR_LINK}} walks through the rest in 2 minutes.

If anything's broken or confusing, please email me. I'd rather fix it than have you
quietly bounce.

— {{YOUR_NAME}}

---

## 4. Known issue — proactive heads-up

Use when you've spotted something problematic but it isn't a true outage.

**Subject:** Heads up: a small thing on Kronoscript

Hey,

Quick note — {{ISSUE_DESCRIPTION e.g. "I noticed mobile uploads of very large videos
are timing out"}}. Affected: {{WHO}}. Workaround: {{WHAT_THEY_CAN_DO_INSTEAD}}.

I'll have a real fix out by {{ETA}}. Not blocking; just wanted you to hear it from me
before you hit it.

— {{YOUR_NAME}}

---

## 5. Outage status — service is down

Don't panic-write this; have it ready to copy-edit-send.

**Subject:** Kronoscript is down — investigating

Hey,

Kronoscript is currently unreachable. I'm aware and investigating.

- Started: {{WHEN_DETECTED}}
- Likely cause: {{BEST_GUESS or "still investigating"}}
- ETA: {{HONEST_ESTIMATE or "I'll send another update within an hour either way"}}

Your data is safe — outages don't touch the database, only the live site.

I'll send a follow-up the moment I have more.

— {{YOUR_NAME}}

---

## Notes for using these

- **First-name personalization matters.** A Mailchimp / SendGrid merge tag for first
  name converts noticeably better than "Hi there."
- **Send from a personal-feeling address** (yours, with a real name) rather than
  noreply@. Increases open rates and reply rates dramatically for invite emails.
- **Don't apologize too much in bug emails.** One sincere line is enough; over-
  apologizing reads as panicky and amplifies the perceived severity.
- **For widespread issues, also post a notice in-app** via the Admin → Tips broadcast
  with the "send as notification" checkbox so users see it without checking email.
