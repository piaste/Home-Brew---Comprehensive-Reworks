-- Home Brew: Recruit certain spawned creatures into the party while a status is present,
-- and aggressively clean them up (remove portrait / party follower) when they die or lose that status.
--
-- Failsafes added:
--   * Removal attempts against ALL players (not only the recorded owner)
--   * Multiple delayed retry passes (handles "dies/despawns quickly" / event-order edge cases)
--   * Periodic sweep to catch anything that slipped past events
--   * ONE-TIME SessionLoaded pass to remove already-stuck DEAD followers from party list

local RECRUIT_STATUSES = {
  ["CCREATURE_SPORE_SERVANT"] = true,
  ["BIND_UNDEAD"] = true,
}

local RECRUIT_DELAY_MS = 500
local CLEANUP_RETRY_DELAYS_MS = { 0, 400, 1200, 2500 }
local SWEEP_INTERVAL_MS = 10000

local Owner = {}
local ActiveStatuses = {}

local function HasAnyActiveStatus(target)
  local t = ActiveStatuses[target]
  if not t then return false end
  for _ in pairs(t) do return true end
  return false
end

local function SafeCall(fn, ...)
  local ok, err = pcall(fn, ...)
  if not ok then
    Ext.Utils.PrintError("[Recruit/Cleanup] Error: " .. tostring(err))
  end
  return ok
end

local function GetAllPlayers()
  local players = {}
  local ok, rows = pcall(function()
    return Osi.DB_Players:Get(nil)
  end)
  if ok and rows then
    for _, row in pairs(rows) do
      local p = row[1]
      if p and p ~= "" then players[#players + 1] = p end
    end
  end
  return players
end

local function HasAnyRecruitStatus(target)
  for status, _ in pairs(RECRUIT_STATUSES) do
    local ok = SafeCall(Osi.HasActiveStatus, target, status)
    if ok and Osi.HasActiveStatus(target, status) == 1 then
      return true
    end
  end
  return false
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

local function AttemptRemovePartyFollower(servant)
  if not servant or servant == "" then return end

  local owner = Owner[servant]
  if owner and owner ~= "" then
    SafeCall(Osi.RemovePartyFollower, servant, owner)
  end

  local players = GetAllPlayers()
  for _, p in ipairs(players) do
    SafeCall(Osi.RemovePartyFollower, servant, p)
  end
end

local function AttemptTidy(servant)
  SafeCall(Osi.SetFaction, servant, Osi.GetBaseFaction(servant))
end

local function Cleanup(servant, reason)
  if not servant or servant == "" then return end

  Owner[servant] = nil
  ActiveStatuses[servant] = nil

  for _, delay in ipairs(CLEANUP_RETRY_DELAYS_MS) do
    Ext.Timer.WaitFor(delay, function()
      AttemptRemovePartyFollower(servant)
      if delay >= 400 then
        AttemptTidy(servant)
      end
      if delay == 0 then
        Ext.Utils.Print("[Recruit/Cleanup] Cleanup started for " .. tostring(servant) .. (reason and (" (" .. reason .. ")") or ""))
      end
    end)
  end
end

-- Conservative one-time cleanup:
-- remove party followers that are DEAD and are NOT real party members/players.
-- This targets stuck portraits without touching legitimate living followers.
local function OneTimeCleanupStuckFollowers()
  local ok, rows = pcall(function()
    -- Usually stored as (Follower, Owner) in BG3
    return Osi.DB_PartyFollowers:Get(nil, nil)
  end)

  if not ok or not rows then
    Ext.Utils.PrintError("[Recruit/Cleanup] DB_PartyFollowers not available or failed; skipping one-time cleanup.")
    return
  end

  local removedCount = 0

  for _, row in pairs(rows) do
    local follower = row[1]
    local owner = row[2]

    if follower and follower ~= "" then
      -- Avoid touching real party members / players.
      local isPlayer = (SafeCall(Osi.IsPlayer, follower) and Osi.IsPlayer(follower) == 1) or false
      local isPartyMember = (SafeCall(Osi.IsPartyMember, follower) and Osi.IsPartyMember(follower) == 1) or false

      if not isPlayer and not isPartyMember then
        local dead = (SafeCall(Osi.IsDead, follower) and Osi.IsDead(follower) == 1) or false

        if dead then
          -- Set a best-guess owner (helps first-pass remove), then remove from all players anyway.
          if owner and owner ~= "" then
            Owner[follower] = owner
          end

          AttemptRemovePartyFollower(follower)
          -- Also schedule retries (covers “stuck until next tick” cases)
          for _, delay in ipairs(CLEANUP_RETRY_DELAYS_MS) do
            if delay > 0 then
              Ext.Timer.WaitFor(delay, function()
                AttemptRemovePartyFollower(follower)
              end)
            end
          end

          removedCount = removedCount + 1
        end
      end
    end
  end

  Ext.Utils.Print("[Recruit/Cleanup] One-time stuck follower cleanup complete. Attempted removals: " .. tostring(removedCount))
end

Ext.Osiris.RegisterListener("StatusApplied", 4, "after",
  function(target, status, causee, storyActionID)
    if not RECRUIT_STATUSES[status] then return end
    if not target or not causee then return end

    ActiveStatuses[target] = ActiveStatuses[target] or {}
    ActiveStatuses[target][status] = true

    if not Owner[target] then
      Owner[target] = causee
    end

    Ext.Timer.WaitFor(RECRUIT_DELAY_MS, function()
      if Owner[target]
        and HasAnyActiveStatus(target)
        and Osi.IsDead(target) == 0
      then
        Recruit(Owner[target], target)
      end
    end)
  end
)

Ext.Osiris.RegisterListener("StatusRemoved", 4, "after",
  function(target, status, causee, storyActionID)
    if not RECRUIT_STATUSES[status] then return end
    if not target then return end

    if ActiveStatuses[target] then
      ActiveStatuses[target][status] = nil
    end

    if (Owner[target] or ActiveStatuses[target]) and Osi.IsDead(target) == 1 then
      Cleanup(target, "StatusRemoved+Dead")
      return
    end

    if (Owner[target] or ActiveStatuses[target]) and not HasAnyActiveStatus(target) then
      Cleanup(target, "StatusRemoved")
    end
  end
)

Ext.Osiris.RegisterListener("Died", 1, "after",
  function(target)
    if not target then return end
    if Owner[target] or ActiveStatuses[target] then
      Cleanup(target, "Died")
    end
  end
)

local function Sweep()
  for servant, _ in pairs(Owner) do
    local dead = (SafeCall(Osi.IsDead, servant) and Osi.IsDead(servant) == 1) or false
    if dead or not HasAnyActiveStatus(servant) then
      Cleanup(servant, "Sweep")
    end
  end

  for servant, _ in pairs(ActiveStatuses) do
    if not Owner[servant] then
      local dead = (SafeCall(Osi.IsDead, servant) and Osi.IsDead(servant) == 1) or false
      if dead or not HasAnyActiveStatus(servant) then
        Cleanup(servant, "Sweep")
      end
    end
  end

  Ext.Timer.WaitFor(SWEEP_INTERVAL_MS, Sweep)
end

Ext.Events.SessionLoaded:Subscribe(function()
  -- Give the save a moment to finish constructing DB tables.
  Ext.Timer.WaitFor(1500, function()
    OneTimeCleanupStuckFollowers()
    Sweep()
  end)
end)