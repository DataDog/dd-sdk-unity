<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="2.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:output method="xml" indent="yes"/>

  <xsl:template match="/test-run">
    <xsl:variable name="suiteFailures" select=".//test-suite[failure and not(@site = ('Child', 'Parent'))]"/>
    <testsuites tests="{@testcasecount + count($suiteFailures)}" failures="{@failed + count($suiteFailures[not(@label = 'Error')])}" errors="{count($suiteFailures[@label = 'Error'])}" disabled="{@skipped}" time="{@duration}">
      <xsl:apply-templates/>
    </testsuites>
  </xsl:template>

  <xsl:template match="test-suite">
    <xsl:variable name="suiteFailure" select="exists(failure) and not(@site = ('Child', 'Parent'))"/>
    <xsl:if test="test-case or $suiteFailure">
      <testsuite tests="{count(test-case) + number($suiteFailure)}" time="{@duration}" errors="{number($suiteFailure and @label = 'Error')}" failures="{count(test-case[failure]) + number($suiteFailure and not(@label = 'Error'))}" skipped="{count(test-case[@runstate = ('Skipped', 'Ignored')])}" timestamp="{@start-time}">
        <xsl:attribute name="name">
          <xsl:for-each select="ancestor-or-self::test-suite[@type='TestSuite']/@name">
            <xsl:value-of select="concat(., '.')"/>
          </xsl:for-each>
        </xsl:attribute>
        <xsl:apply-templates select="test-case"/>
        <xsl:if test="$suiteFailure">
          <testcase name="{if (@site = 'SetUp') then 'initializationError' else 'executionError'}" classname="{if (@fullname) then @fullname else @name}" time="0">
            <xsl:element name="{if (@label = 'Error') then 'error' else 'failure'}">
              <xsl:attribute name="message" select="failure/message"/>
              <xsl:value-of select="concat(failure/message, '&#10;', failure/stack-trace)"/>
            </xsl:element>
          </testcase>
        </xsl:if>
      </testsuite>
    </xsl:if>
    <xsl:apply-templates select="test-suite"/>
  </xsl:template>

  <xsl:template match="test-case">
    <testcase name="{@name}" assertions="{@asserts}" time="{@duration}" status="{@result}" classname="{@classname}">
      <xsl:if test="@runstate = 'Skipped' or @runstate = 'Ignored'">
        <skipped/>
      </xsl:if>
      
      <xsl:apply-templates/>
    </testcase>
  </xsl:template>

  <xsl:template match="command-line"/>
  <xsl:template match="settings"/>
  <xsl:template match="filter"/>

  <xsl:template match="output">
    <system-out>
      <xsl:value-of select="."/>
    </system-out>
  </xsl:template>

  <xsl:template match="stack-trace">
  </xsl:template>

  <xsl:template match="test-case/failure">
    <failure message="{./message}">
      <xsl:value-of select="./stack-trace"/>
    </failure>
  </xsl:template>

  <xsl:template match="test-suite/failure"/>

  <xsl:template match="test-case/reason">
    <xsl:if test="./message != null">
      <skipped message="{./message}"/>
    </xsl:if>
  </xsl:template>
  
  <xsl:template match="test-case/assertions">
  </xsl:template>

  <xsl:template match="test-suite/reason"/>

  <xsl:template match="properties"/>
</xsl:stylesheet>
