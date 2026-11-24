<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Developer Certificate of Origin

Contributions to EQLMaker are accepted under the Developer Certificate of Origin (DCO),
version 1.1. There is no contributor licence agreement and nobody gets special rights to your
contribution: it enters the project under the licence of the project (AGPL-3.0-or-later), and
you keep your copyright.

## What it means, in plain words

By signing off a commit you state that

- you wrote the change yourself, or you have the right to pass it on under the licence of the
  project (for example because it comes from code under a compatible open licence), and
- you know that the contribution and your sign-off are public and stay in the history.

It is a statement about where the change comes from. It is not a transfer of rights.

## How to sign off

Add the line with `-s`:

```text
git commit -s -m "Describe the change"
```

Git then appends a line with the name and e-mail address of your Git configuration:

```text
Signed-off-by: Your Name <you@example.org>
```

Use your real name and an address you can be reached at. Every commit of a pull request needs
the line; the check "Commits signed off (DCO)" of the repository (`scripts/check-dco.ps1`)
refuses a pull request without it. To add it later:

```text
git commit --amend -s              (the last commit)
git rebase --signoff main          (all commits of the branch)
```

## The text

The certificate itself, unchanged, from https://developercertificate.org/ :

```text
Developer Certificate of Origin
Version 1.1

Copyright (C) 2004, 2006 The Linux Foundation and its contributors.

Everyone is permitted to copy and distribute verbatim copies of this
license document, but changing it is not allowed.


Developer's Certificate of Origin 1.1

By making a contribution to this project, I certify that:

(a) The contribution was created in whole or in part by me and I
    have the right to submit it under the open source license
    indicated in the file; or

(b) The contribution is based upon previous work that, to the best
    of my knowledge, is covered under an appropriate open source
    license and I have the right under that license to submit that
    work with modifications, whether created in whole or in part
    by me, under the same open source license (unless I am
    permitted to submit under a different license), as indicated
    in the file; or

(c) The contribution was provided directly to me by some other
    person who certified (a), (b) or (c) and I have not modified
    it.

(d) I understand and agree that this project and the contribution
    are public and that a record of the contribution (including all
    personal information I submit with it, including my sign-off) is
    maintained indefinitely and may be redistributed consistent with
    this project or the open source license(s) involved.
```
