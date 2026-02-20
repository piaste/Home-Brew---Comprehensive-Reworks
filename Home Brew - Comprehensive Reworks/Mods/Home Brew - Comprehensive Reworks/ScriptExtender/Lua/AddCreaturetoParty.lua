-- Add any statuses you want to trigger the "add to party" behavior here.
local RECRUIT_STATUSES = {
  ["CCREATURE_SPORE_SERVANT"] = true,
  ["BIND_UNDEAD"] = true,
}

-- Delay before recruiting (useful for cases like Spore Servant where resurrection happens first).
local RECRUIT_DELAY_MS = 500

-- Track recruited creatures:
--   Owner[target] = recruiting character
--   ActiveStatuses[target][status] = true while the status is present
local Owner = {}
local ActiveStatuses = {}

local function HasAnyActiveStatus(target)
  local t = ActiveStatuses[target]
  if not t then return false end
  for _ in pairs(t) do
    return true
  end
  return false
end

local function SafeCall(fn, ...)
  local ok, err = pcall(fn, ...)
  if not ok then
    Ext.Utils.PrintError("[Recruit/Cleanup] Error: " .. tostring(err))
  end
end

local function Recruit(owner, servant)
  if not owner or not servant then return end
  if Osi.IsDead(servant) == 1 then return end

  Osi.SetFaction(servant, Osi.GetFaction(owner))
  Osi.AddPartyFollower(servant, owner)
  Osi.SetFollowCharacter(servant, owner)
  Osi.MakePlayerActive(servant)

  Owner[servant] = owner
end

local function Cleanup(servant)
  local owner = Owner[servant]

  if owner then
    -- If this fails (sometimes does on dead/despawning entities), don't let it abort the rest.
    SafeCall(Osi.RemovePartyFollower, servant, owner)
  end

  -- Optional, but tidy. Can fail in edge cases; keep it safe.
  SafeCall(Osi.SetFaction, servant, Osi.GetBaseFaction(servant))

  Owner[servant] = nil
  ActiveStatuses[servant] = nil
end

-- When a configured status is applied, wait a moment (if desired) then recruit.
Ext.Osiris.RegisterListener("StatusApplied", 4, "after",
  function(target, status, causee, storyActionID)
    if not RECRUIT_STATUSES[status] then return end
    if not target or not causee then return end

    ActiveStatuses[target] = ActiveStatuses[target] or {}
    ActiveStatuses[target][status] = true

    -- First applier becomes the owner (same behavior as the original single-status script).
    if not Owner[target] then
      Owner[target] = causee
    end

    Ext.Timer.WaitFor(RECRUIT_DELAY_MS, function()
      -- Status might have been removed during the delay; also don't recruit corpses.
      if Owner[target]
        and HasAnyActiveStatus(target)
        and Osi.IsDead(target) == 0
      then
        Recruit(Owner[target], target)
      end
    end)
  end
)

-- When a configured status is removed, only de-party once *all* configured statuses are gone.
-- IMPORTANT FIX: if the target is already dead (common for spore removal), clean up immediately.
Ext.Osiris.RegisterListener("StatusRemoved", 4, "after",
  function(target, status, causee, storyActionID)
    if not RECRUIT_STATUSES[status] then return end
    if not target then return end

    if ActiveStatuses[target] then
      ActiveStatuses[target][status] = nil
    end

    -- FIX: If they're dead now, always cleanup right away (don't wait for other statuses).
    if Owner[target] and Osi.IsDead(target) == 1 then
      Cleanup(target)
      return
    end

    -- Normal behavior: only clean up once ALL configured recruit-statuses are gone.
    if Owner[target] and not HasAnyActiveStatus(target) then
      Cleanup(target)
    end
  end
)

-- Backup: if they die normally, also clean up.
Ext.Osiris.RegisterListener("Died", 1, "after",
  function(target)
    if not target then return end
    if Owner[target] then
      Cleanup(target)
    end
  end
)
