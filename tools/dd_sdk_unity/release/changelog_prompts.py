# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""PR grouping, synthesis, and cleanup prompts from dd-sdk-cpp."""

from collections.abc import Callable

from pydantic import BaseModel, ConfigDict, Field

from .models import Change


class GroupedPR(BaseModel):
    model_config = ConfigDict(extra="forbid")
    number: int = Field(description="Integer PR number. Use the original PR number exactly.")
    title: str = Field(
        description="Single-line string describing the PR. Use the original text exactly."
    )


class PRGroup(BaseModel):
    model_config = ConfigDict(extra="forbid")
    label: str = Field(
        description="Short string describing the purpose of this group of PRs. "
        "Infer from the titles of constituent PRs."
    )
    prs: list[GroupedPR] = Field(
        description="Subset of PRs from the original list that belong to this group."
    )


class GroupedPRs(BaseModel):
    model_config = ConfigDict(extra="forbid")
    groups: list[PRGroup]


class ChangelogEntry(BaseModel):
    model_config = ConfigDict(extra="forbid")
    text: str = Field(
        description="Full text of a single-line changelog entry, formatted as markdown."
    )


class ChangelogEntryList(BaseModel):
    model_config = ConfigDict(extra="forbid")
    breaking_changes: list[ChangelogEntry] = Field(
        description="Entries for changes that remove or incompatibly alter a public API or "
        "previously-documented behavior, requiring users to update their code on upgrade. "
        "When an entry qualifies as both a breaking change and a feature or fix, place it "
        "here rather than in the other lists."
    )
    features: list[ChangelogEntry] = Field(
        description="Entries for new capabilities, APIs, configuration options, or other additive "
        "changes that do not break existing usage."
    )
    fixes: list[ChangelogEntry] = Field(
        description="Entries for corrections to incorrect behavior, data, or crashes that do not "
        "break existing usage."
    )


def run_grouped_prs_prompt(
    prompt: Callable[[str, type[BaseModel]], BaseModel], prs: list[Change]
) -> GroupedPRs:
    pr_text = "\n".join(f"{pr.number:5d} {pr.title}" for pr in prs)
    result = GroupedPRs.model_validate(
        prompt(GROUPED_PRS_PROMPT_TEMPLATE % {"prs": pr_text}, GroupedPRs)
    )
    expected = {pr.number for pr in prs}
    got = {pr.number for group in result.groups for pr in group.prs}
    if expected != got:
        raise ValueError(
            "LLM response PR numbers do not match input: "
            f"expected {','.join(str(n) for n in expected)}; got {','.join(str(n) for n in got)}"
        )
    return result


def format_pr_for_prompt(pr: Change) -> str:
    return "\n".join(
        [
            f'<pr number="{pr.number}">',
            f"<title>{pr.title}</title>",
            "<body>",
            pr.body,
            "</body>",
            "</pr>",
        ]
    )


def run_changelog_entry_list_prompt(
    prompt: Callable[[str, type[BaseModel]], BaseModel],
    group_label: str,
    group_prs: list[Change],
) -> ChangelogEntryList:
    pr_text = "\n".join(format_pr_for_prompt(pr) for pr in group_prs)
    return ChangelogEntryList.model_validate(
        prompt(
            CHANGELOG_ENTRY_LIST_PROMPT % {"group_label": group_label, "prs": pr_text},
            ChangelogEntryList,
        )
    )


def _format_entries_for_prompt(entries: list[ChangelogEntry]) -> str:
    if not entries:
        return "(none)"
    return "\n".join(f"{i + 1}. {entry.text}" for i, entry in enumerate(entries))


def run_cleanup_prompt(
    prompt: Callable[[str, type[BaseModel]], BaseModel], changelog: ChangelogEntryList
) -> ChangelogEntryList:
    return ChangelogEntryList.model_validate(
        prompt(
            CLEANUP_PROMPT_TEMPLATE
            % {
                "breaking_changes": _format_entries_for_prompt(changelog.breaking_changes),
                "features": _format_entries_for_prompt(changelog.features),
                "fixes": _format_entries_for_prompt(changelog.fixes),
            },
            ChangelogEntryList,
        )
    )


GROUPED_PRS_PROMPT_TEMPLATE = """You are preparing to write a customer-facing changelog for an SDK. Below is a list of PRs that have been merged since the last release.

Your task: group these PRs so that closely-related PRs can be considered together. A group should capture a coherent user-visible change: for example, several PRs that together implement one feature, or a fix that directly relates to a feature in the same release. PRs with no clear relationship to others should each be their own group.

Your response will be a JSON array where each element is an object representing a related group of one or more PRs. This JSON value will adhere to the provided schema. Your response will include no prose or formatting.

Use only the data provided below. Do not consult external sources.

<prs>
%(prs)s
</prs>"""


CHANGELOG_ENTRY_LIST_PROMPT = """You are writing a customer-facing changelog for a Datadog Unity SDK. You have been given a group of related pull requests that together represent a single logical change.

Your task: synthesize 0 or more changelog entries describing the user-visible impact of this change on developers who use the SDK as a library dependency.

Include an entry for:
- New or modified public APIs: functions, types, configuration options, or   constants added, changed, or removed
- Observable behavior changes: anything a developer would notice at runtime,   including bug fixes that corrected incorrect data, crashes, or wrong behavior
- Breaking changes to any public API or previously-documented behavior

Omit entries for:
- Internal refactors, test infrastructure, or build system changes
- Changes to private implementation details that are not reachable through the   public API and whose effects are not observable to library consumers
- chore work with no user-visible effect
- Sub-changes that are already fully captured by another entry in this group

If every change in the group falls into the omit list, return empty lists for all three categories. An empty response is correct and preferred over producing a thin or redundant entry.

A group may produce multiple entries only when it contains genuinely distinct user-facing changes that each deserve to be called out separately. When in doubt, prefer a single consolidated entry over splitting.

Categorize each entry as exactly one of:
- breaking_changes: removes or incompatibly alters a public API or   previously-documented behavior, requiring users to update their code on upgrade.   When an entry qualifies as both a breaking change and a feature or fix,   categorize it as a breaking change.
- features: adds new capability, APIs, or configuration options without breaking   existing usage.
- fixes: corrects incorrect behavior, data, or crashes without breaking existing   usage.

Style:
- Write from the user's perspective — describe the impact, not the implementation.
- Use present tense: e.g. "Crash timestamps are now reported in milliseconds."
- Be concise: one sentence is almost always sufficient.
- Do not reference PR numbers, branch names, or internal type names.
- Format the entry as plain markdown (inline `code` for public API identifiers;   no headers or bullet lists within an entry).

Your response will adhere to the provided schema. Include no prose or formatting outside the JSON.

Use only the data provided below. Do not consult external sources. Ignore boilerplate sections in PR bodies (e.g. checklists, test plans, or template scaffolding) — focus on the description of what changed and why.

<group label="%(group_label)s">
%(prs)s
</group>"""


CLEANUP_PROMPT_TEMPLATE = """You are performing a final editorial pass on a draft changelog for a Datadog Unity SDK. The entries below were generated independently for each group of related PRs and then merged; as a result, they may contain redundancies, inconsistencies in tone or style, or suboptimal ordering.

Your tasks:

1. Deduplicate. Remove or merge entries that describe the same user-visible    change. Pay particular attention to cross-category duplication: if a    breaking_changes entry already captures a change, remove the corresponding    feature or fix entry rather than keeping both. If two entries partially overlap,    consolidate them into one entry in whichever category is most appropriate.

2. Normalize tone and style. All entries should:
   - Be written from the user's perspective — describe the impact, not the      implementation.
   - Use present tense: e.g. "X now does Y."
   - Be concise: one sentence is almost always sufficient.
   - Use inline `code` only for public API identifiers.
   - Not reference PR numbers, branch names, or internal type names.

3. Sort entries within each category. Order primarily by importance: the most    significant and impactful changes first. As a secondary concern, place related    entries adjacent to one another.

Do not invent entries that were not present in the input. Produce output in the same three-category schema.

Your response will adhere to the provided schema. Include no prose or formatting outside the JSON.

Use only the data provided below. Do not consult external sources.

### Breaking Changes
%(breaking_changes)s

### Features
%(features)s

### Fixes
%(fixes)s"""
