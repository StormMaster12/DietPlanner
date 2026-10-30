# Visual baselines

`VisualRegressionTests` takes a full-page screenshot of each page and compares it with the PNG of the
same name in this folder. A test fails if more than 0.1% of its pixels differ noticeably. The test
also fails when a baseline is missing. In that case it writes the missing baseline, so a new
screenshot can't get through CI until it has been committed and reviewed.

## Changing the UI on purpose

The baselines are rendered by CI's Linux Chromium (`ubuntu-24.04`). Your machine's fonts and
anti-aliasing are different, so don't generate baselines locally. On Windows and macOS the
comparisons are skipped.

1. Push your branch and open a PR. CI fails on the screenshots that changed and uploads a
   `visual-diffs` artifact with an `expected`, `actual` and `diff` image for each one. Changed
   pixels are red in the diff image.
2. If the changes are intended, add the **`update-visual-baselines`** label to the PR. The
   *Update visual baselines* workflow then does the following:
   - regenerates every baseline
   - commits any PNGs that changed to your branch
   - re-runs CI
   - removes the label
3. The reviewer approves the new images in the PR's *Files changed* tab. GitHub shows the old and
   new PNGs side by side, and also has swipe and onion-skin views.

You can also run the workflow by hand against a branch from the Actions tab. It refuses to run on
`master`, so baselines always change through a PR.

## Adding a screenshot

Add a test to `VisualRegressionTests` that calls `ExpectPageToMatchBaselineAsync("<page>.<viewport>")`.
Make anything that changes from run to run fixed first. For example, the week plan's dates are
replaced with placeholder text. Then use the label as described above to create the baseline.
