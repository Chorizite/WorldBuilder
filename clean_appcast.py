#!/usr/bin/env python3
"""
Remove all releases from a specific channel in a Sparkle appcast.xml file.

Usage:
    python clean_appcast.py <appcast_file> <channel_name>

Example:
    python clean_appcast.py appcast.xml edge
"""

import xml.etree.ElementTree as ET
import sys
from pathlib import Path

# Sparkle namespace
SPARKLE_NS = "http://www.andymatuschak.org/xml-namespaces/sparkle"
ET.register_namespace('sparkle', SPARKLE_NS)


def clean_appcast(appcast_path: str, channel_to_remove: str) -> None:
    """
    Remove all items with the specified channel from the appcast.
    
    Args:
        appcast_path: Path to the appcast.xml file
        channel_to_remove: Name of the channel to remove (e.g., 'edge')
    """
    try:
        tree = ET.parse(appcast_path)
        root = tree.getroot()
        
        removed_count = 0
        
        # Find all channel elements in the RSS feed
        for channel in root.findall('.//channel'):
            items = channel.findall('item')
            for item in items:
                # Look for the sparkle:channel element
                channel_elem = item.find(f'{{{SPARKLE_NS}}}channel')
                if channel_elem is not None and channel_elem.text == channel_to_remove:
                    channel.remove(item)
                    removed_count += 1
        
        # Write back to file
        tree.write(appcast_path, encoding='utf-8', xml_declaration=True)
        
        print(f"✓ Removed {removed_count} '{channel_to_remove}' channel release(s) from {appcast_path}")
        
    except FileNotFoundError:
        print(f"✗ File not found: {appcast_path}", file=sys.stderr)
        sys.exit(1)
    except ET.ParseError as e:
        print(f"✗ Failed to parse XML: {e}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"✗ Unexpected error: {e}", file=sys.stderr)
        sys.exit(1)


def main():
    if len(sys.argv) != 3:
        print("Usage: python clean_appcast.py <appcast_file> <channel_name>")
        print("Example: python clean_appcast.py appcast.xml edge")
        sys.exit(1)
    
    appcast_path = sys.argv[1]
    channel_name = sys.argv[2]
    
    clean_appcast(appcast_path, channel_name)


if __name__ == "__main__":
    main()