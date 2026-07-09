# PR Automation

This repository uses automated checks to make pull requests easier for maintainers and safer for third-party contributions.

## Required PR Body

Every PR should fill in the template and check the `SIGN` declaration. The `PR Gate` workflow verifies:

- The PR has a meaningful title and summary.
- The `SIGN` declaration is checked.
- Third-party PRs do not change privileged automation, packaging, script, or binary artifact paths without maintainer handling.

The gate uses `pull_request_target` but does not check out or execute contributor code.

## ChatGPT Preliminary Review

`ChatGPT PR Review` runs automatically when a PR is opened or updated. Maintainers can request another pass by commenting:

```text
@ChatGPT
```

or:

```text
/chatgpt-review
```

The workflow reads PR metadata and diff through the GitHub API, then posts or updates one bot comment with the preliminary review. It does not execute pull request code.

## OpenAI Configuration

Set these repository secrets or variables:

- `OPENAI_API_KEY` secret: required for ChatGPT review. If absent, the review job skips safely.
- `OPENAI_BASE_URL` secret: optional OpenAI-compatible gateway URL, for example a relay ending in `/v1`. If absent, the official OpenAI API base URL is used.
- `OPENAI_REVIEW_MODEL` repository variable: optional model override. If absent, the workflow uses its built-in default.

The review workflow tries the Responses API first. If a gateway does not support it, the workflow falls back to an OpenAI-compatible chat completions endpoint.
