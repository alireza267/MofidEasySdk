# Contributing to MofidEasySdk

Thanks for helping improve the SDK. This guide explains how to get a change into `main`.

## How changes reach `main`

Nobody pushes to `main` directly. Every change goes through a **pull request** (GitHub's name for a merge request):

1. You make your change on a branch in your fork.
2. You open a pull request against `main`.
3. CI builds and tests it (`build-and-test` check).
4. The code owner ([@alireza267](https://github.com/alireza267)) reviews and approves it.
5. The code owner squash-merges it.

A pull request can only be merged when all of these are true:

- The `build-and-test` check passes on the latest commit, and the branch is up to date with `main`.
- The code owner has approved it, **after** your most recent push. Pushing new commits resets the approval.
- Every review comment thread is resolved.

## 1. Set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). The exact version is pinned in `global.json`.

1. Click **Fork** on the [repository page](https://github.com/alireza267/MofidEasySdk).
2. Clone your fork and add the original repository as `upstream`:

   ```
   git clone https://github.com/<your-username>/MofidEasySdk.git
   cd MofidEasySdk
   git remote add upstream https://github.com/alireza267/MofidEasySdk.git
   ```

3. Check that everything builds and the tests pass:

   ```
   dotnet build
   dotnet test
   ```

## 2. Make your change

1. Start from the latest `main`:

   ```
   git fetch upstream
   git switch -c <branch-name> upstream/main
   ```

   Name the branch after what it does: `feature/list-open-orders`, `fix/token-expiry-check`, `docs/readme-errors`.

2. Make the change. Keep each pull request to **one** topic, because small pull requests get reviewed faster.

3. Follow the project's conventions:
   - Match the style of the surrounding code. The build must have **no warnings**, because CI treats warnings as errors.
   - Every public type and member needs an XML doc comment (`///`).
   - Add or update tests in `tests/MofidEasySdk.Tests` for every behavior change. Tests use `RecordingHandler` (see `TestSupport.cs`) and must **never** call the real API.
   - If you change the public API or its behavior, update `README.md`.
   - Request bodies must match the real API exactly. If you change one, include a sample request (with the token removed) in the pull request description.
   - Don't add automatic retries to add or edit order calls. A retried request can place a duplicate order.

4. Run the same checks CI runs:

   ```
   dotnet build -c Release -warnaserror
   dotnet test -c Release
   ```

## 3. Commit

Write commit messages in the imperative mood, with a short first line:

```
Add ListOpenOrdersAsync

Calls GET core/api/v2/orders and maps the result to OpenOrder records.
```

> **Never commit secrets.** That includes access tokens (JWTs starting with `eyJ`), account numbers and real order IDs. If you commit one by accident, tell the code owner. Deleting it in a new commit isn't enough, because it stays in the git history. Log out of EasyTrader to invalidate the token.

## 4. Open the pull request

1. Push your branch to your fork:

   ```
   git push -u origin <branch-name>
   ```

2. On GitHub, click **Compare & pull request**. Make sure the base is `alireza267/MofidEasySdk` → `main`.
3. Fill in the template: what the change does, why, and how you tested it.
4. If the work isn't ready for review yet, open it as a **draft** pull request.

## 5. Review

- The code owner is requested as a reviewer automatically (see `.github/CODEOWNERS`).
- Reply to each review comment, push fixes as new commits, and resolve the thread once it's handled. Don't force-push during review, because it makes changes harder to follow.
- If `main` moves ahead, update your branch:

  ```
  git fetch upstream
  git merge upstream/main
  git push
  ```

- Once approved and green, the code owner squash-merges your pull request. The pull request title becomes the commit message on `main`, so keep it clear.

## Reporting bugs and ideas

Open an issue with:

- What you did: the SDK call and its arguments, with the token removed.
- What you expected, and what happened, including the exception type and message.
- The SDK version (`MofidEasySdk` package version) and .NET version.

For a security problem, such as a way to leak tokens, **don't open a public issue**. Contact the code owner directly.
