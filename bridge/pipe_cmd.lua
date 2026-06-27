local names = {
  'Executor_Super_Star_Destroyer',
  'Eclipse_Super_Star_Destroyer',
  'Executor',
  'EXECUTOR',
  'Super_Star_Destroyer',
  'Imperial_Star_Destroyer',
  'Imperial_Star_Destroyer_Two',
  'Pellaeon_Star_Destroyer',
  'Bellator_Star_Dreadnought',
  'Praetor_Battlecruiser',
  'Assertor_Dreadnought',
  'Allegiance_Battlecruiser',
  'Victory_Star_Destroyer'
}
local found = ''
for i = 1, table.getn(names) do
  local t = Find_Object_Type(names[i])
  if t then
    found = found .. names[i] .. ' YES, '
  end
end
if found == '' then
  SWFOC_Log('None found')
else
  SWFOC_Log('Found: ' .. found)
end
