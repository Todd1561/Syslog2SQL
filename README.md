# Syslog2SQL

A very basic Syslog server for collecting logs and saving them to a SQL server.  Compatible with both BSD style messages or the newer IETF.  Runs as a Windows service.  Tested with MS SQL server, but should work with others.

## Getting Started

1. Download the latest build from https://github.com/Todd1561/Syslog2SQL/releases and save it to a folder on your computer.
1. Modify `syslog2sql.cfg` to suit your needs.
1. Install the Syslog2SQL service by running `C:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe Syslog2SQL.exe`
1. Remember to open the UDP port you decide to use (514 by default) on any relevant firewalls.
1. Start the service, which will automatically create a Syslog2SQL table in the database you specified in the config file.
1. Check the Windows Event Log for any error messages, events will be logged under the source `Syslog2SQL`.

## Author
Todd Nelson  
todd@toddnelson.net  
https://toddnelson.net