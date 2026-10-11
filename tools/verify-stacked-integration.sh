#!/usr/bin/env bash
# Verify that every reviewed workstream is included in the proposed
# Android integration commit. This does not replace on-device acceptance.
set -euo pipefail

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Not a Git checkout" >&2
  exit 2
fi

declare -a expected=(
  '16:6da443520853c527f2f6e4601bc6db46bbcd4a04'
  '17:16a1c0af3fbd32b75c82e629bdf079b5d8ad2a2a'
  '18:f907775ca656fa8215c33744d296e5e6e973f163'
  '19:a98abc3c4eed3f6b0ba5c87cef7340416f5b6ce1'
  '20:93eb020be1ee7bfa09b3f964a503f6dd7c0418ad'
  '21:2a1c306acd5e876848f2cdae0a48c4d32c69cde6'
  '22:0348fdb35ce8b4edb60ef8fb76f54109acb98ba9'
  '23:a39251af2f45a7a2495fcb4c205f8061f30db597'
  '24:5b9dd669875dc16ea0a693ce53325bccfa2a24ab'
  '25:523362b6ea510693ae509b73f8c72e799a80e916'
  '26:c21d01bd7073c5792d47a1d18f66f48dfcc9cb65'
  '27:1b9c07df489bb5c18616f48d67ea1178f0fdbade'
)
for item in "${expected[@]}"; do
  pr="${item%%:*}"
  sha="${item#*:}"
  if ! git cat-file -e "${sha}^{commit}"; then
    echo "Missing commit object for PR #${pr}: ${sha}" >&2
    exit 1
  fi
  if ! git merge-base --is-ancestor "${sha}" HEAD; then
    echo "Integration is missing PR #${pr}: ${sha}" >&2
    exit 1
  fi
  echo "PR #${pr}: included (${sha:0:12})"
done

main_base='af0fb026f9816c27acfb369508b9f7ad972df6dd'
if ! git merge-base --is-ancestor "${main_base}" HEAD; then
  echo "Integration no longer descends from the approved main baseline." >&2
  exit 1
fi
echo "Integration ancestry verification: PASS (${#expected[@]} PR heads)."
