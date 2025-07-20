-- Run this query to check your language database
SELECT Id, Name, NameGeo 
FROM "ContentDb"."Languages" 
ORDER BY Id;

-- This will show you the actual ID to language mappings
-- Expected results should be:
-- 1 | English  | ინგლისური
-- 2 | Georgian | ქართული  
-- 7 | Russian  | რუსული

-- If Russian has ID 2 instead of 7, that's your problem! 