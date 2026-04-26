This should be a simple VSIX VS2026 extension to analyze standard ILogger usage and scan for 
log writes missing an EvenId (the number one) or duplicates.
WARNING: the "code fix" stuff DOES NOT WORK (yet?)


TODO
* Unit test for dupes
* Fixers (all)
* Remove all that "make uppercase" default from hello world analyzer cruft