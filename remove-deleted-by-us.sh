#!/usr/bin/env bash
git status --porcelain -z |
while IFS= read -r -d '' entry; do
    status=${entry:0:2}
    file=${entry:3}

    case "$status" in
        DU)
            git rm -- "$file"
            ;;
        "A ")
            case "$file" in
                *.[xX][mM][lL]) ;;
                *) git rm -f -- "$file" ;;
            esac
            ;;
    esac
done
