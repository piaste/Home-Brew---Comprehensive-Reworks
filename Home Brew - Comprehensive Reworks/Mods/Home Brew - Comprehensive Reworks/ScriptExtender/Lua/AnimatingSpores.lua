local SPORE_STATUS = "CCREATURE_SPORE_SERVANT"
local SporeOwner = {}

local function Recruit(owner, servant)
  if not owner or not servant then return end
  if Osi.IsDead(servant) == 1 then return end

  Osi.SetFaction(servant, Osi.GetFaction(owner))
  Osi.AddPartyFollower(servant, owner)
  Osi.SetFollowCharacter(servant, owner)
  Osi.MakePlayerActive(servant)

  SporeOwner[servant] = owner
end

local function Cleanup(servant)
  local owner = SporeOwner[servant]
  if owner then
    Osi.RemovePartyFollower(servant, owner)
  end

  -- optional, but tidy:
  Osi.SetFaction(servant, Osi.GetBaseFaction(servant))

  SporeOwner[servant] = nil
end

-- When spores are applied, resurrection happens; delay then recruit.
Ext.Osiris.RegisterListener("StatusApplied", 4, "after",
  function(target, status, causee, storyActionID)
    if status ~= SPORE_STATUS then return end
    if not target or not causee then return end

    Ext.Timer.WaitFor(500, function()
      if Osi.IsDead(target) == 0 then
        Recruit(causee, target)
      end
    end)
  end
)

-- If the curse is removed, your status will be removed, and the status will kill them.
-- This listener ensures we de-party them cleanly as soon as the status is stripped.
Ext.Osiris.RegisterListener("StatusRemoved", 4, "after",
  function(target, status, causee, storyActionID)
    if status ~= SPORE_STATUS then return end
    if not target then return end
    if SporeOwner[target] then
      Cleanup(target)
    end
  end
)

-- Backup: if they die normally, also clean up.
Ext.Osiris.RegisterListener("Died", 1, "after",
  function(target)
    if not target then return end
    if SporeOwner[target] then
      Cleanup(target)
    end
  end
)
